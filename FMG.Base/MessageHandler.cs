using System.Collections.Generic;
using System.Threading.Tasks;
using FMG.Serverless.Logging;
using FMG.ServiceBus;

namespace FMG.Serverless.MessageHandlers
{
    public abstract class MessageHandler<TMessage, TAppStore> : IMessageHandler<TMessage> where TMessage : IMessage
    {

        /// <summary>
        /// Processes the async.
        /// </summary>
        /// <returns>The async.</returns>
        /// <param name="message">Message.</param>
        /// <param name="logTrace">Log trace.</param>
        public abstract Task<IEnumerable<IMessage>> ProcessAsync(TMessage message, ILogTrace logTrace);

        /// <summary>
        /// Get AppStore singleton instance
        /// </summary>
        protected TAppStore AppStore { get; private set; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="appStore"></param>
        protected MessageHandler(TAppStore appStore)
        {
            AppStore = appStore;
        }
    }
}
