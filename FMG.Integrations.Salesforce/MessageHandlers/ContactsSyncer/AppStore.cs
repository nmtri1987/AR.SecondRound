using FMG.ExternalServices.Clients.SalesforceService;
using FMG.Serverless.ServiceClients.OAuthTokenManager;
using FMG.Serverless.ServiceClients.OAuthTokenManager.Models;
using FMG.Serverless.ServiceClients.OAuthTokenV2;
using FMG.Serverless.ServiceClients.RemoteData;
using FMG.Serverless.ServiceClients.SyncSettingV2;
using FMG.Serverless.Utilities;
using FMG.ServiceBus;
using StackExchange.Redis;

namespace FMG.Integrations.Salesforce.MessageHandlers.ContactsSyncer
{
    public class AppStore : SingletonAppStore<Config>
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="config"></param>
        public AppStore(Config config) : base(config)
        {
            OAuthTokenServiceClient = SingletonStore<OAuthTokenV2ServiceClient>
                .Get(() => new OAuthTokenV2ServiceClient(Config));

            SyncSettingV2ServiceClient = SingletonStore<SyncSettingV2ServiceClient>
                .Get(() => new SyncSettingV2ServiceClient(Config));

            ServiceBusClient = SingletonStore<SQSServiceBusClient>
                .Get(() => new SQSServiceBusClient(Config.ServiceBusClientConfig));

            SalesforceOAuthServiceClient = new SalesforceOAuthServiceClient(
                Config.SalesforceOAuthServiceClientConfig
            );

            OAuthTokenManager = new OAuthTokenManager(new OAuthTokenManagerConfig
            {
                IntegrationType = CRMIntegrationTypes.Salesforce,
                AWSRegionEndpoint = Config.AWSRegionEndpoint,
                AWSSecretKey = Config.AWSSecretKey,
                AWSAccessKey = Config.AWSAccessKey,
                AWSAccountId = Config.AWSAccountId,
                OAuthServiceClient = SalesforceOAuthServiceClient,
                SlackAlertChannel = Config.SlackAlertRefreshTokenChannel
            });

            if (!string.IsNullOrEmpty(Config.RedisConnectionString))
                RedisDB = ConnectionMultiplexer.Connect(Config.RedisConnectionString).GetDatabase();

            RemoteDataServiceClient = SingletonStore<RemoteDataServiceClient>.Get(
                () => new RemoteDataServiceClient(Config)
            );
        }

        /// <summary>
        /// OAuth token service client
        /// </summary>
        public IOAuthTokenV2ServiceClient OAuthTokenServiceClient { get; set; }

        /// <summary>
        /// SyncSetting service use to
        ///     Get sync setting to check RemoteGroupIds (sync all or sync by specify topics)
        /// </summary>
        public ISyncSettingV2ServiceClient SyncSettingV2ServiceClient { get; set; }

        /// <summary>
        /// ServiceBus use to
        ///     Publish contacts to FMGContactUpdater
        /// </summary>
        public IServiceBusClient ServiceBusClient { get; set; }

        /// <summary>
        /// Salesforce OAuth
        /// </summary>
        public SalesforceOAuthServiceClient SalesforceOAuthServiceClient { get; set; }

        /// <summary>
        /// OAuth Token Manager
        /// </summary>
        public IOAuthTokenManager OAuthTokenManager { get; set; }

        /// <summary>
        /// The redis database use to 
        ///     Write Total Contact get from Salesforce
        ///     Write Processed Count = 0
        /// </summary>
        public IDatabase RedisDB { get; set; }

        /// <summary>
        /// RemoteData service client use to 
        ///     Get remote contact ids belong at least 1 group
        /// </summary>
        public IRemoteDataServiceClient RemoteDataServiceClient { get; set; }
    }
}
