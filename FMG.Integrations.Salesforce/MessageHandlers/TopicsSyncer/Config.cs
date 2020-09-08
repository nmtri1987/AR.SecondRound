using FMG.ExternalServices.Clients.SalesforceService.Models;
using FMG.Serverless.Utilities.Helpers;
using FMG.Serverless.Utilities.Helpers.Config;
using FMG.Serverless.Utilities.Helpers.Parsers;

namespace FMG.Integrations.Salesforce.MessageHandlers.TopicsSyncer
{
    public class Config : AWSConfigBase
    {
        /// <summary>
        /// Contact service
        /// </summary>
        public string ContactsServiceApiEndpoint { get; set; }

        /// <summary>
        /// We need to include the RemoteDataConfig because it's an in-memory service for this worker
        /// </summary>
        public Shared.Services.RemoteData.Config RemoteDataConfig { get; set; }

        public SalesforceOAuthServiceClientConfig SalesforceOAuthServiceClientConfig { get; set; }

        /// <summary>The Slack channel to alert when Refresh Token failed.</summary>
        public string SlackAlertRefreshTokenChannel { get; set; }

        /// <summary>
        /// Load config from env var
        /// </summary>
        /// <returns></returns>
        public static Config LoadConfigFromEnvVar()
        {
            var awsConfig = EnvVarParser.GetAWSBaseConfig();

            return new Config
            {
                AWSAccessKey = awsConfig.AWSAccessKey,
                AWSAccountId = awsConfig.AWSAccountId,
                AWSRegionEndpoint = awsConfig.AWSRegionEndpoint,
                AWSSecretKey = awsConfig.AWSSecretKey,
                ContactsServiceApiEndpoint = EnvVarParser.GetRequiredString("CONTACTS_SERVICE_API_ENDPOINT"),
                RemoteDataConfig = Shared.Services.RemoteData.Config.LoadConfigFromEnvVar(),
                SalesforceOAuthServiceClientConfig = SalesforceOAuthServiceClientConfig.LoadConfigFromEnvVar(),
                SlackAlertRefreshTokenChannel = EnvVarParser.GetRequiredString("SLACK_ALERT_REFRESH_TOKEN_CHANNEL")
            };
        }

        #region Implement IConfig

        public new virtual void Validate()
        {
            base.Validate();
            ValidationHelpers.ValidateRequiredProp(nameof(ContactsServiceApiEndpoint), ContactsServiceApiEndpoint);
            RemoteDataConfig.Validate();
            ValidationHelpers.ValidateRequiredProp(nameof(SlackAlertRefreshTokenChannel), SlackAlertRefreshTokenChannel);
        }

        #endregion
    }
}