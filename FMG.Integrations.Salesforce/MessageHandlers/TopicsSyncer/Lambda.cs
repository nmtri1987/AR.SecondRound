using FMG.Serverless.MessageHandlers;
using FMG.ServiceBus.MessageModels;

namespace FMG.Integrations.Salesforce.MessageHandlers.TopicsSyncer
{
    /// <summary>
    /// Entrypoint for SalesforceTopicsSyncer
    /// </summary>
    public class Lambda : SQSLambdaMessageHandler<SalesforceTopicsSyncerMessage, AppStore, Config>
    {
        public override Config LoadConfigFromEnvVar => Config.LoadConfigFromEnvVar();

        public override IMessageHandler<SalesforceTopicsSyncerMessage> Handler => new Handler(AppStore);

        public override string TopicName => TopicNames.SALESFORCE_TOPICS_SYNCER;
    }
}