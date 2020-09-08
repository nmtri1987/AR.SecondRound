using FMG.Serverless.Logging;
using FMG.Serverless.Utilities;
using FMG.Serverless.Utilities.Helpers.Config;
using System;

namespace FMG.Serverless
{
    /// <summary>
    /// This class will initialize singleton instance for AppStore from service config.
    /// Service config can be either Lambda service (HTTP server) or Lambda message handler (process message from SNS or SQS)
    /// </summary>
    /// <typeparam name="TAppStore"></typeparam>
    /// <typeparam name="TConfig"></typeparam>
    public abstract class LambdaServiceWithAppStore<TAppStore, TConfig> where TConfig : IConfig where TAppStore : SingletonAppStore<TConfig>
    {
        #region Protected Properties

        /// <summary>
        /// Contains configs and services.
        /// </summary>
        protected TAppStore AppStore { get; private set; }

        #endregion

        #region Constructors

        /// <summary>
        /// Constructor is used by Lambda function
        /// It's required by Lambda
        /// </summary>
        protected LambdaServiceWithAppStore() => Init(LoadConfigFromEnvVar);

        /// <summary>
        /// Constructor is used by unit test
        /// </summary>
        /// <param name="config"></param>
        protected LambdaServiceWithAppStore(TConfig config) => Init(config);

        /// <summary>
        /// Init for Constructor
        /// </summary>
        /// <param name="config"></param>
        private void Init(TConfig config)
        {
            GlobalConfig.LoadFromEnvVar();
            config.Validate();
            AppStore = InitAppStore(config);// InitAppStore(config);
        }

        #endregion

        /// <summary>
        /// Init app store
        /// </summary>
        /// <returns></returns>
        protected TAppStore InitAppStore(TConfig config) => (TAppStore)Activator.CreateInstance(typeof(TAppStore), config);

        /// <summary>
        /// Load config from env var
        /// </summary>
        /// <returns></returns>
        public abstract TConfig LoadConfigFromEnvVar { get; }
    }
}
