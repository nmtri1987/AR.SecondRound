using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.Lambda.SQSEvents;
using FMG.Serverless.Logging;
using FMG.Serverless.Utilities;
using FMG.Serverless.Utilities.Helpers.Config;
using FMG.ServiceBus;
using Newtonsoft.Json;

namespace FMG.Serverless.MessageHandlers
{
    public abstract class
        SQSLambdaMessageHandler<TMessage, TAppStore, TConfig> : LambdaMessageHandler<TMessage, TAppStore, TConfig>
        where TMessage : IMessage
        where TConfig : IConfig
        where TAppStore : SingletonAppStore<TConfig>
    {
        #region Implement LambdaMessageHandler

        public override async Task<IList<TMessage>> ParseMessages(object reqData, ILogTrace logTrace)
        {
            var strMessage = JsonConvert.SerializeObject(reqData);
            logTrace?.Add(LogLevel.Debug, "ParseMessages()", new { strMessage });

            var sqsEvent = JsonConvert.DeserializeObject<SQSEvent>(strMessage);
            logTrace?.Add(LogLevel.Info, "ParseMessages()", "Parsed SQSEvent");

            return ParseMessages(sqsEvent);
        }

        /// <summary>
        /// Parse the SQSEvent to a list of messages
        /// </summary>
        /// <param name="sqsEvent"></param>
        /// <returns>The list of TMEssages</returns>
        /// <exception cref="Exception"></exception>
        IList<TMessage> ParseMessages(SQSEvent sqsEvent)
        {
            if (sqsEvent.Records == null || !sqsEvent.Records.Any())
            {
                throw new Exception("Failed to parse message from sqsEvent");
            }

            return sqsEvent.Records.Select(
                record =>
                {
                    // parse the json string to TMessage
                    var message = JsonConvert.DeserializeObject<TMessage>(record.Body);

                    // validate message schema
                    message.Validate();

                    return message;
                }
            ).ToList();
        }

        #endregion
    }
}