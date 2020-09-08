using FMG.ExternalServices.Clients.SalesforceService.Models;
using FMG.Serverless.Utilities.Helpers;
using FMG.Serverless.Utilities.Helpers.Config;
using FMG.Serverless.Utilities.Helpers.Parsers;
using FMG.ServiceBus.MessageModels;

namespace FMG.Integrations.Salesforce.MessageHandlers.ContactsSyncer
{
    public class Config : AWSConfigBase
    {
        public ServiceBusClientConfig ServiceBusClientConfig { get; set; }

        public int SyncHoursOffset { get; set; }

        public SalesforceOAuthServiceClientConfig SalesforceOAuthServiceClientConfig { get; set; }

        public bool ForceFullSync { get; set; }

        public string RedisConnectionString { get; set; }

        /// <summary>The Slack channel to alert when Refresh Token failed.</summary>
        public string SlackAlertRefreshTokenChannel { get; set; }

        #region Public methods

        /// <summary>
        /// Loads the configuration from env variable.
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
                ServiceBusClientConfig = new ServiceBusClientConfig
                {
                    AWSAccessKey = awsConfig.AWSAccessKey,
                    AWSAccountId = awsConfig.AWSAccountId,
                    AWSRegionEndpoint = awsConfig.AWSRegionEndpoint,
                    AWSSecretKey = awsConfig.AWSSecretKey
                },
                SyncHoursOffset = EnvVarParser.GetInteger("SYNC_HOURS_OFFSET", 2),
                SalesforceOAuthServiceClientConfig = SalesforceOAuthServiceClientConfig.LoadConfigFromEnvVar(),
                ForceFullSync = EnvVarParser.GetBoolean("SALESFORCE_FORCE_FULL_SYNC"),
                RedisConnectionString = EnvVarParser.GetRequiredString("REDIS_REDLOCK_CONNECTION_STRING"),
                SlackAlertRefreshTokenChannel = EnvVarParser.GetRequiredString("SLACK_ALERT_REFRESH_TOKEN_CHANNEL")
            };
        }

        #endregion

        #region Implement IConfig

        /// <summary>
        /// Validate Config properties
        /// </summary>
        public override void Validate()
        {
            base.Validate();
            ValidationHelpers.ValidateRequiredProp(nameof(RedisConnectionString), RedisConnectionString);
            ValidationHelpers.ValidateRequiredProp(nameof(SlackAlertRefreshTokenChannel), SlackAlertRefreshTokenChannel);
        }

        #endregion
    }
}
