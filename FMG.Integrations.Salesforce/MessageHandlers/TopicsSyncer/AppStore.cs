using FMG.ExternalServices.Clients.ContactsService;
using FMG.ExternalServices.Clients.SalesforceService;
using FMG.Integrations.Salesforce.MessageHandlers.TopicsSyncer.Helpers;
using FMG.Serverless.ServiceClients.OAuthTokenManager;
using FMG.Serverless.ServiceClients.OAuthTokenManager.Models;
using FMG.Serverless.ServiceClients.OAuthTokenV2;
using FMG.Serverless.ServiceClients.RemoteData;
using FMG.Serverless.ServiceClients.SyncSettingV2;
using FMG.Serverless.Utilities;

namespace FMG.Integrations.Salesforce.MessageHandlers.TopicsSyncer
{
    public class AppStore : SingletonAppStore<Config>
    {
        public AppStore(Config config) : base(config)
        {
            // service clients
            SyncSettingV2ServiceClient = SingletonStore<SyncSettingV2ServiceClient>.Get(
                () => new SyncSettingV2ServiceClient(Config)
            );
            RemoteDataServiceClient = SingletonStore<RemoteDataServiceClient>.Get(
                () => new RemoteDataServiceClient(Config)
            );
            ContactsServiceClient = SingletonStore<ContactsServiceClient>.Get(
                () => new ContactsServiceClient(new ContactsServiceConfig
                { ApiEndpoint = Config.ContactsServiceApiEndpoint })
            );
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

            // comparers
            SalesforceTopicMappingComparer = SingletonStore<SalesforceTopicMappingComparer>.Get(
                () => new SalesforceTopicMappingComparer()
            );
        }

        /// <summary>
        /// Salesforce OAuth
        /// </summary>
        public SalesforceOAuthServiceClient SalesforceOAuthServiceClient { get; set; }

        /// <summary>
        /// OAuth Token Manager
        /// </summary>
        public IOAuthTokenManager OAuthTokenManager { get; set; }

        /// <summary>
        /// Contact Service
        /// </summary>
        public IContactsServiceClient ContactsServiceClient { get; set; }

        /// <summary>
        /// In-memory RemoteData service client
        /// </summary>
        public IRemoteDataServiceClient RemoteDataServiceClient { get; set; }

        /// <summary>
        /// OAuth service client
        /// </summary>
        public IOAuthTokenV2ServiceClient OAuthTokenServiceClient =>
            SingletonStore<OAuthTokenV2ServiceClient>.Get(
                () => new OAuthTokenV2ServiceClient(Config)
            );

        /// <summary>
        /// SyncSetting service client
        /// </summary>
        public ISyncSettingV2ServiceClient SyncSettingV2ServiceClient { get; set; }

        /// <summary>
        /// Comparer for 2 SalesforceTopicMapping objects
        /// </summary>
        public SalesforceTopicMappingComparer SalesforceTopicMappingComparer { get; set; }
    }
}