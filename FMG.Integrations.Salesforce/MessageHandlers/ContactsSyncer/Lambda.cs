using FMG.Serverless.MessageHandlers;
using FMG.Serverless.Utilities;
using FMG.ServiceBus.MessageModels;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: Amazon.Lambda.Core.LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace FMG.Integrations.Salesforce.MessageHandlers.ContactsSyncer
{
    public class Lambda : SQSLambdaMessageHandler<SalesforceContactsSyncerMessage, AppStore, Config>
    {
        /// <summary>
        /// Gets the name of the topic.
        /// </summary>
        /// <value>The name of the topic.</value>
        public override string TopicName => TopicNames.SALESFORCE_CONTACTS_SYNCER;

        /// <summary>
        /// Gets the handler.
        /// </summary>
        /// <value>The handler.</value>
        public override IMessageHandler<SalesforceContactsSyncerMessage> Handler
            => SingletonStore<Handler>.Get(() => new Handler(AppStore));

        /// <summary>
        /// Load config from env var
        /// </summary>
        public override Config LoadConfigFromEnvVar => Config.LoadConfigFromEnvVar();
    }
}
