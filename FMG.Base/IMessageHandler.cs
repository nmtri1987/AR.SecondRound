using System.Collections.Generic;
using System.Threading.Tasks;
using FMG.Serverless.Logging;
using FMG.ServiceBus;

namespace FMG.Serverless.MessageHandlers
{

    /// <summary>
    /// Message handler.
    /// </summary>
    public interface IMessageHandler<TMessage> where TMessage : IMessage
    {
        /// <summary>
        /// Processes the async.
        /// </summary>
        /// <returns>The async.</returns>
        /// <param name="message">Message.</param>
        /// <param name="logTrace">Log trace.</param>
        Task<IEnumerable<IMessage>> ProcessAsync(TMessage message, ILogTrace logTrace);
    }
}
