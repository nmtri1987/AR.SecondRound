using System.Collections.Generic;

namespace FMG.Integrations.Salesforce.MessageHandlers.TopicsSyncer.Helpers
{
    /// <summary>
    /// Class for comparing the 2 SalesforceTopicMapping objects
    /// </summary>
    public class SalesforceTopicMappingComparer : IEqualityComparer<SalesforceTopicMapping>
    {
        public bool Equals(SalesforceTopicMapping x, SalesforceTopicMapping y)
        {
            return x.SalesforceTopicId == y.SalesforceTopicId;
        }

        public int GetHashCode(SalesforceTopicMapping obj)
        {
            return obj.SalesforceTopicId.GetHashCode();
        }
    }
}