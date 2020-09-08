using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.SQS.Model;
using FMG.Serverless.Exceptions;
using FMG.Serverless.Loggers;
using FMG.Serverless.Logging;
using FMG.Serverless.ServiceClients.Slack;
using FMG.Serverless.ServiceClients.Slack.Models;
using FMG.Serverless.Utilities;
using FMG.Serverless.Utilities.Helpers.Config;
using FMG.Serverless.Utilities.Helpers.Parsers;
using FMG.ServiceBus;
using FMG.ServiceBus.MessageModels;
using Newtonsoft.Json;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;

namespace FMG.Serverless.MessageHandlers
{
    /// <summary>
    /// Lambda message handler.
    /// </summary>
    public abstract class
        LambdaMessageHandler<TMessage, TAppStore, TConfig> : LambdaServiceWithAppStore<TAppStore, TConfig>
        where TMessage : IMessage
        where TConfig : IConfig
        where TAppStore : SingletonAppStore<TConfig>
    {
        #region Protected Properties

        /// <summary>
        /// The service bus client
        /// </summary>
        protected IServiceBusClient SvcServiceBus
            => SingletonStore<SQSServiceBusClient>.Get(()
                =>
            {
                var config = ServiceBusClientConfig.LoadConfigFromEnvVar();
                return new SQSServiceBusClient(config);
            });

        protected CustomLogTraceConfig CustomLogTraceConfig = CustomLogTraceConfig.LoadConfigFromEnvVar();

        // slack
        protected bool SlackAlertEnabled = EnvVarParser.GetBoolean("SLACK_ALERT_ENABLED", false);
        protected string SlackAlertChannel = EnvVarParser.GetString("SLACK_ALERT_CHANNEL", "#fmg-alert-integration");
        protected int? SqsMaximumReceives = EnvVarParser.GetInteger("MESSAGE_MAX_RETRY_COUNT");

        protected ISlackServiceClient
            SvcSlack = new SlackServiceClient(SlackServiceClientConfig.LoadConfigFromEnvVar());

        #endregion

        #region Redis Redlock props

        /// <summary>
        /// List of connection info to Redis Redlock server
        /// </summary>
        protected List<EnvVarParser.ConnectionInfo> RedisRedlockServers =
            EnvVarParser.ParseConnectionStrings("REDIS_REDLOCK_CONNECTION_STRING");

        /// <summary>
        /// The expiry time of the lock key (if applicable), default to 5 seconds
        /// </summary>
        protected TimeSpan RedisRedlockExpiryTime =
            TimeSpan.FromSeconds(EnvVarParser.GetInteger("REDIS_REDLOCK_EXPIRY_SECOND", 5));

        /// <summary>
        /// The number of seconds wait when obtains a key is locked.
        /// </summary>

        protected int? RedisRedlockWaitSenconds = EnvVarParser.GetInteger("REDIS_REDLOCK_WAIT_SECOND");

        /// <summary>
        /// The number of seconds for each time retry.
        /// </summary>
        protected int? RedisRedlockRetrySeconds = EnvVarParser.GetInteger("REDIS_REDLOCK_RETRY_SECOND");

        private RedLockFactory _redLockFactory;

        public RedLockFactory RedLockFactory
        {
            get
            {
                if (_redLockFactory == null)
                {
                    // Check whether the redlock is configured properly
                    if (!RedisRedlockServers.Any()) throw new RedisRedlockNotConfiguredException();

                    var endPoints = RedisRedlockServers
                        .Select(server => new RedLockEndPoint(new DnsEndPoint(server.Host, server.Port))).ToList();
                    _redLockFactory = RedLockFactory.Create(endPoints);
                }

                return _redLockFactory;
            }
        }

        #endregion

        #region Abstract Methods

        /// <summary>
        /// Gets the handler.
        /// </summary>
        /// <value>The handler.</value>
        public abstract IMessageHandler<TMessage> Handler { get; }

        /// <summary>
        /// Gets the name of the topic.
        /// </summary>
        /// <value>The name of the topic.</value>
        public abstract string TopicName { get; }

        /// <summary>
        /// Parse message object send from APIGateway/SNS Topic to messages to handle.
        /// </summary>
        /// <returns>The messages.</returns>
        /// <param name="reqData">The lambda request data event object</param>
        /// <param name="logTrace">Log trace.</param>
        public abstract Task<IList<TMessage>> ParseMessages(object reqData, ILogTrace logTrace);

        /// <summary>
        /// Handle the specified and context.
        /// This is the entry point to be invoked in Lambda
        /// </summary>
        /// <returns>The handle.</returns>
        /// <param name="reqData">Lambda Request data event object</param>
        /// <param name="context">Context.</param>
        public async Task HandleAsync(object reqData, ILambdaContext context)
        {
            // build ILogTrace instance
            CustomLogTraceConfig.Logger = new Loggers.LambdaLogger(context.Logger);
            var logTrace = new MessageHandlerLogTrace(
                CustomLogTraceConfig,
                context.AwsRequestId,
                Handler.GetType().Name,
                TopicName
            );

            // debug request data
            logTrace.Add(LogLevel.Debug, "HandleAsync()", new { reqData });
            IList<TMessage> messages;

            // Catch exception here and alert to slack
            // The exception is related to parse message.
            try
            {
                // parse messages from snsEvent object
                messages = await ParseMessages(reqData, logTrace);

                // no messages, return
                if (!messages.Any())
                {
                    logTrace.Add(LogLevel.Warning, "HandleAsync()", "No messages");
                    return;
                }

                logTrace.Add(
                    LogLevel.Info,
                    "HandleAsync()",
                    $"Received {StringHelpers.Pluralize(messages.Count, "message")}"
                );
            }
            catch (Exception e)
            {
                logTrace.AddError(e);

                //Slack alert if enabled
                AlertErrorToSlack(new { reqData }, e, logTrace);

                // flush log to CloudWatch, Loggly
                logTrace.Flush();

                throw e;
            }

            // Catch exception when handle for each messaage.
            // We don't alert to slack here because we already alert for each message
            try
            {
                // process messages concurrently
                logTrace.Add(LogLevel.Debug, "ProcessMessages()", "Starting...");
                await ProcessMessagesAsync(messages, reqData, context);
                logTrace.Add(LogLevel.Debug, "ProcessMessages()", "Completed");
            }
            catch (Exception ex)
            {
                // don't alert error to Slack here b/c we already alert in process handle a message.
                logTrace.AddError(ex);

                throw ex;
            }
            finally
            {
                // flush log to CloudWatch, Loggly
                logTrace.Flush();
            }
        }

        #endregion

        /// <summary>
        /// Process all the messages async
        /// </summary>
        /// <returns>The messages async.</returns>
        /// <param name="messages">Messages.</param>
        protected async Task ProcessMessagesAsync(IList<TMessage> messages, object reqData, ILambdaContext context)
        {
            if (messages != null)
            {
                await Task.WhenAll(messages.Select(message => ProcessMessageAsync(message, reqData, context)));
            }
        }

        /// <summary>
        /// Process individual the message async (with error handler)
        /// </summary>
        /// <returns>The message async.</returns>
        /// <param name="message">Message.</param>
        async Task ProcessMessageAsync(TMessage message, object reqData, ILambdaContext context)
        {
            // build log trace
            var logTrace = BuildLogTraceForMessageHandler(message, context.AwsRequestId);
            try
            {
                // check whether we need to lock the message
                var lockKey = message.BuildLockKey();
                logTrace.Add(LogLevel.Info, "Message LockKey", lockKey);

                if (string.IsNullOrWhiteSpace(lockKey))
                {
                    // no need to lock the message, process immediately
                    await ProcessMessageAsync(message, logTrace);
                    return;
                }

                // make sure lockKey case-insensitive
                lockKey = $"topic:{message.TopicName}-{lockKey}".ToLower();
                var startedAt = DateTime.UtcNow;


                async Task HandleRedlockAsync(RedLockNet.IRedLock redlock)
                {
                    if (!redlock.IsAcquired)
                    {
                        throw new CannotObtainLockException(lockKey, startedAt);
                    }
                    await ProcessMessageAsync(message, logTrace);
                }


                // blocking and retrying every retry seconds until the lock is available, or wait seconds have passed
                if (RedisRedlockWaitSenconds != null && RedisRedlockRetrySeconds != null)
                {
                    var waitTime = TimeSpan.FromSeconds(RedisRedlockWaitSenconds.Value);
                    var retryTime = TimeSpan.FromSeconds(RedisRedlockRetrySeconds.Value);
                    using (var redlock = await RedLockFactory.CreateLockAsync(lockKey, RedisRedlockExpiryTime, waitTime, retryTime))
                    {
                        await HandleRedlockAsync(redlock);
                    }
                    return;
                }

                // try locking and process the message
                using (var redlock = await RedLockFactory.CreateLockAsync(lockKey, RedisRedlockExpiryTime))
                {
                    await HandleRedlockAsync(redlock);
                }
            }
            // Handled exception, the message should be ignored to process in this case
            catch (IgnoreProcessingMessageException ex)
            {
                // Simply log warning and do nothing
                logTrace?.Add(LogLevel.Warning, ex.LogTitle, ex.LogMessage);
            }
            // Unhandled Exception
            catch (Exception ex)
            {
                // handle error
                logTrace?.Add(LogLevel.Error, "Failed to process message", new { ex.Message, ex.StackTrace });

                CheckThenAlertErrorToSlack(message, reqData, ex, logTrace);

                // rethrow the message to retry later
                throw ex;
            }
            finally
            {
                // flush log into CloudWatch, Loggly
                logTrace?.Flush();
            }
        }

        /// <summary>
        /// Processes the message and send next messages if any
        /// </summary>
        /// <returns>The message async.</returns>
        /// <param name="message">Message.</param>
        /// <param name="logTrace">Log trace.</param>
        async Task ProcessMessageAsync(TMessage message, ILogTrace logTrace)
        {
            // for debugging if failed to process the message
            logTrace?.Add(LogLevel.Debug, "ProcessMessageAsync()", new { message });

            // process message based on business logic
            var nextMessages = await Handler.ProcessAsync(message, logTrace);

            // publish next messages to bus
            // (delegate next logic into another async workers)
            if (nextMessages != null && nextMessages.Any())
            {
                await SvcServiceBus.PublishMessages(nextMessages, logTrace);
            }
        }

        /// <summary>
        /// Builds the log trace for each Message to pass to MessageHandler instance
        /// </summary>
        /// <returns>The log trace.</returns>
        /// <param name="message">Message.</param>
        ILogTrace BuildLogTraceForMessageHandler(TMessage message, string awsRequestId)
        {
            var logTrace = new MessageLogTrace(
                CustomLogTraceConfig,
                awsRequestId,
                message,
                Handler.GetType().Name
            );

            var extendedProps = message.BuildLogProps();
            if (extendedProps != null && extendedProps.Any())
            {
                logTrace.ExtendProps(extendedProps);
            }

            return logTrace;
        }

        #region Slack Alert

        /// <summary>
        /// Checks the then alert error to slack.
        /// </summary>
        /// <param name="jsonMsg">Json message.</param>
        /// <param name="ex">Ex.</param>
        /// <param name="logTrace">Log trace.</param>
        protected void CheckThenAlertErrorToSlack(TMessage jsonMsg, object reqData, Exception ex, ILogTrace logTrace)
        {
            if (!IsAlertToSlack(jsonMsg, reqData, logTrace)) return;

            AlertErrorToSlack(jsonMsg, ex, logTrace);
        }

        /// <summary>
        /// Alert an exception to Slack
        /// </summary>
        /// <param name="message"></param>
        /// <param name="ex"></param>
        /// <param name="logTrace"></param>
        private void AlertErrorToSlack(object message, Exception ex, ILogTrace logTrace)
        {
            // don't alert to Slack
            if (!SlackAlertEnabled || string.IsNullOrWhiteSpace(SlackAlertChannel)) return;

            // alert to Slack on background
            var slackMsg = new SlackMessage
            {
                Channel = SlackAlertChannel,
                Username = $"MessageHandler:{Handler.GetType().Name}",
                Texts = new List<string>
                {
                    "```",
                    $"Failed to process message. Error: {ex.Message}",
                    JsonConvert.SerializeObject(message, Formatting.Indented),
                    $"StackTrace: {ex.StackTrace}",
                    "```"
                }
            };
            Task.WaitAll(SvcSlack.SendWithErrorHandler(slackMsg, logTrace));
        }

        /// <summary>
        /// Check the ApproximateReceiveCount with MaximumRetryCount
        /// </summary>
        /// <param name="message"></param>
        /// <param name="reqData"></param>
        /// <param name="logTrace"></param>
        /// <returns></returns>
        private bool IsAlertToSlack(TMessage message, object reqData, ILogTrace logTrace)
        {
            // alert to Slack if there is no config for SQS Maximum Retry.
            if (SqsMaximumReceives == null) return true;

            try
            {
                var sqsEvent = JsonConvert.DeserializeObject<SQSEvent>(JsonConvert.SerializeObject(reqData));

                // Find the SQS message body have same message id  that we generated for a message.
                var sqsMessage = sqsEvent.Records.FirstOrDefault(x => JsonConvert.DeserializeObject<TMessage>(x.Body).MessageId.Equals(message.MessageId));

                var isApproximateReceiveCount = sqsMessage.Attributes.TryGetValue("ApproximateReceiveCount", out string approximateReceiveCount);
                logTrace.ExtendProps(new Dictionary<string, object> { { "approximateReceiveCount", approximateReceiveCount } });

                // Check the ApproximateReceiveCount of message.
                if (sqsMessage != null && isApproximateReceiveCount
                    && int.Parse(approximateReceiveCount) == SqsMaximumReceives)
                {
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                logTrace.Add(LogLevel.Warning, "IsAlertToSlack()", new { ex.Message, ex.StackTrace });
                // Alert to Slack if have any exception when check the ApproximateReceiveCount.
                return true;
            }
        }

        #endregion
    }
}