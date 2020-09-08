namespace FMG.Integrations.Salesforce.MessageHandlers.TopicsSyncer.Helpers
{
    /// <summary>
    /// A model class representing the mapping relationship between the Salesforce Topic and the FMG Group
    /// This class act as a common class for both the SalesforceTopic and the RemoteGroupMapping so we can compare those
    /// 2 objects
    /// </summary>
    public class SalesforceTopicMapping
    {
        /// <summary>
        /// Use this prop to compare whether the 2 items are the same
        /// </summary>
        public string SalesforceTopicId { get; set; }

        /// <summary>
        /// Topic name in Salesforce
        /// </summary>
        public string SalesforceTopicName { get; set; }

        /// <summary>
        /// A prop to represent the internal group id in the FMG system
        /// </summary>
        public int FMGGroupId { get; set; }
    }
}