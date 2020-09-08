using FMG.ExternalServices.Clients.ContactsService.Models;
using FMG.ExternalServices.Clients.SalesforceServiceV2;
using FMG.Integrations.Salesforce.MessageHandlers.TopicsSyncer.Helpers;
using FMG.Integrations.Salesforce.Utilities;
using FMG.Serverless.Exceptions;
using FMG.Serverless.Logging;
using FMG.Serverless.MessageHandlers;
using FMG.Serverless.ServiceClients.OAuthTokenV2.Models;
using FMG.Serverless.ServiceClients.RemoteData.Models;
using FMG.Serverless.ServiceClients.SyncSettingV2.Models;
using FMG.Serverless.Utilities;
using FMG.ServiceBus;
using FMG.ServiceBus.MessageModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace FMG.Integrations.Salesforce.MessageHandlers.TopicsSyncer
{
    public class Handler : MessageHandler<SalesforceTopicsSyncerMessage, AppStore>
    {
        public Handler(AppStore appStore) : base(appStore)
        {
        }

        /// <inheritdoc />
        public override async Task<IEnumerable<IMessage>> ProcessAsync(SalesforceTopicsSyncerMessage message,
            ILogTrace logTrace)
        {
            var partyId = message.PartyId;

            // get the sync setting first
            var syncSetting = await VerifySyncStatusAsync(partyId, logTrace);

            var isSyncAll = syncSetting.IsSyncAll;
            var isFullSync = syncSetting.IsFullSync;
            logTrace?.Add(LogLevel.Info, "Sync Setting", new { isSyncAll, isFullSync });

            // init the salesforce client
            var svcSalesforce = InitSalesforceServiceClientAsync(syncSetting.OAuthToken, logTrace);

            // get the list of SalesforceCampaigns and RemoteGroupMapping
            // these 2 methods both return a list of SalesforceTopicMapping so we can compare those 2 and do some
            // operations like Intersection, Difference,...

            // NOTE: Initially, we support Salesforce Topics as Groups so these methods all return
            // SalesforceTopicMapping. However, we changed to support Salesforce Campaigns as Groups later. We didn't
            // want to change a lot of code here so we just changes the getSFGroupMappingFunc to get the Salesforce
            // Campaigns instead of the Topics but it still returns the Topic schema.
            var isSyncCampaign = true;
            Func<Task<IList<SalesforceTopicMapping>>> getSFGroupMappingFunc =
                () => GetAllSalesforceTopicsAsync(partyId, svcSalesforce, logTrace);
            if (isSyncCampaign)
            {
                if (syncSetting.IntegrationType == CRMIntegrationTypes.Practifi)
                    getSFGroupMappingFunc = () => GetAllPractifiCampaignsAsync(partyId, svcSalesforce, logTrace);
                else
                    getSFGroupMappingFunc = () => GetAllSalesforceCampaignsAsync(partyId, svcSalesforce, logTrace);
            }

            var groupMappingResults =
                await Task.WhenAll(getSFGroupMappingFunc(), GetAllRemoteGroupMappingsAsync(partyId, logTrace));

            // the TopicMappings from Salesforce, these objects don't have the FMGGroupId prop
            var salesforceTopicMappings = groupMappingResults.ElementAt(0);
            // the TopicMappings retrieved from RemoteData service, these objects have the linked FMGGroupId prop
            var remoteTopicMappings = groupMappingResults.ElementAt(1);
            // the list of GroupIds that user selected on UI
            var userSelectedGroupIds = syncSetting.RemoteGroupIDs ?? new List<string>();

            // add new groups
            await InsertNewGroupsAsync(
                partyId, salesforceTopicMappings, remoteTopicMappings, userSelectedGroupIds, logTrace
            );

            // delete non-existing groups
            await DeleteGroupsAsync(partyId, salesforceTopicMappings, remoteTopicMappings, userSelectedGroupIds,
                logTrace);

            // update the names for all the groups that remain from the sync
            await UpdateGroupNames(partyId, salesforceTopicMappings, remoteTopicMappings, userSelectedGroupIds,
                logTrace);

            return new List<SalesforceTopicAssignmentsSyncerMessage>
            {
                new SalesforceTopicAssignmentsSyncerMessage { PartyId = partyId }
            };
        }

        #region Internal Helpers

        #region Update groups methods

        /// <summary>
        /// Update group names for the groups that remain from last sync
        /// </summary>
        /// <param name="partyId"></param>
        /// <param name="salesforceTopicMappings"></param>
        /// <param name="remoteTopicMappings"></param>
        /// <param name="logTrace"></param>
        /// <returns></returns>
        protected async Task UpdateGroupNames(int partyId,
            IList<SalesforceTopicMapping> salesforceTopicMappings,
            IList<SalesforceTopicMapping> remoteTopicMappings,
            IList<string> userSelectedGroupIds,
            ILogTrace logTrace)
        {
            var startedAt = DateTime.UtcNow;

            // find the groups that remain from last sync
            // Cannot use IList.Intersection because the salesforceTopicMapping items don't contain
            // the FMGGroupId while the remoteTopicMappings items don't contain the new name from Salesforce
            var remainGroups = new List<SalesforceTopicMapping>();
            foreach (var remoteTopicMapping in remoteTopicMappings)
            {
                //
                var salesforceTopicMapping = salesforceTopicMappings.FirstOrDefault(item =>
                    item.SalesforceTopicId == remoteTopicMapping.SalesforceTopicId);

                if (salesforceTopicMapping == null) continue;

                salesforceTopicMapping.FMGGroupId = remoteTopicMapping.FMGGroupId;
                remainGroups.Add(salesforceTopicMapping);
            }

            // is user selected to sync only some groups, filter to those groups only
            if (userSelectedGroupIds.Any())
            {
                var userSelectedGroups = userSelectedGroupIds.Select(
                    groupId => new SalesforceTopicMapping { SalesforceTopicId = groupId }
                ).ToList();
                remainGroups = remainGroups.Intersect(userSelectedGroups, AppStore.SalesforceTopicMappingComparer)
                    .ToList();
            }

            // update FMG Group
            var groupTasks = remainGroups.Select(group => AppStore.ContactsServiceClient.UpdateGroupAsync(
                new UpdateGroupRequest { GroupID = group.FMGGroupId, Name = group.SalesforceTopicName }
            )).ToList();
            foreach (var task in groupTasks)
            {
                await task;
            }

            logTrace.Add(LogLevel.Info, "Update FMG Groups", $"{groupTasks.Count()} updated", startedAt);

            // update RemoteGroupMapping
            startedAt = DateTime.UtcNow;
            var remoteTasks = remainGroups.Select(group =>
                AppStore.RemoteDataServiceClient.UpsertRemoteGroupMappingAsync(
                    new UpsertRemoteGroupMappingRequestData
                    {
                        GroupId = group.FMGGroupId,
                        PartyId = partyId,
                        RemoteGroupId = group.SalesforceTopicId,
                        RemoteGroupName = group.SalesforceTopicName
                    },
                    logTrace
                )).ToList();
            foreach (var task in remoteTasks)
            {
                await task;
            }

            logTrace.Add(LogLevel.Info, "Update RemoteGroupMapping", $"{remoteTasks.Count()} updated", startedAt);
        }

        #endregion

        #region Insert new groups methods

        protected async Task InsertNewGroupsAsync(int partyId,
            IList<SalesforceTopicMapping> salesforceTopicMappings,
            IList<SalesforceTopicMapping> remoteTopicMappings,
            IList<string> filteredRemoteGroupIds,
            ILogTrace logTrace)
        {
            var startedAt = DateTime.UtcNow;

            // logging
            logTrace?.Add(LogLevel.Debug, "salesforceTopicTopicMappings", salesforceTopicMappings
                .Select(topic => new { topic.SalesforceTopicId, topic.SalesforceTopicName })
                .ToList());

            // find the new topics
            var newTopicMappings =
                salesforceTopicMappings.Except(remoteTopicMappings, AppStore.SalesforceTopicMappingComparer).ToList();
            logTrace?.Add(LogLevel.Debug, "newtopicMappings",
                newTopicMappings.Select(topic => new { topic.SalesforceTopicId, topic.SalesforceTopicName }).ToList());

            if (filteredRemoteGroupIds.Any())
            {
                var filteredTopicMappings = filteredRemoteGroupIds.Select(
                    remoteGroupId => new SalesforceTopicMapping { SalesforceTopicId = remoteGroupId });
                newTopicMappings =
                    newTopicMappings.Intersect(filteredTopicMappings, AppStore.SalesforceTopicMappingComparer).ToList();
                logTrace?.Add(LogLevel.Debug, "newtopicMappings after intersect",
                    newTopicMappings.Select(topic => new { topic.SalesforceTopicId, topic.SalesforceTopicName })
                        .ToList());
            }

            // logging
            logTrace?.Add(
                LogLevel.Info,
                "New Groups",
                newTopicMappings.Select(topic => topic.SalesforceTopicName).ToList()
            );

            // insert these as FMGGroups
            var tasks = newTopicMappings.Select(newTopic => InsertNewGroupAsync(partyId, newTopic, logTrace)).ToList();
            await Task.WhenAll(tasks);

            // log
            logTrace?.Add(
                LogLevel.Info, "InsertNewGroupsAsync", $"{newTopicMappings.Count()} new groups inserted", startedAt
            );
        }

        /// <summary>
        /// Insert 1 single FMGGroup + RemoteGroupMapping
        /// </summary>
        /// <param name="partyId"></param>
        /// <param name="newTopic"></param>
        /// <param name="logTrace"></param>
        protected async Task InsertNewGroupAsync(int partyId, SalesforceTopicMapping newTopic, ILogTrace logTrace)
        {
            // insert FMG Group
            var res = await AppStore.ContactsServiceClient.AddGroupAsync(
                new AddGroupRequest { PartyID = partyId, Name = newTopic.SalesforceTopicName }
            );

            logTrace.Add(LogLevel.Info, "InsertNewGroupAsync() - Done"
                , new { newTopic.SalesforceTopicName, res.Status });

            if (res.Status != HttpStatusCode.Created)
            {
                logTrace?.Add(LogLevel.Error, "InsertNewGroupAsync() - Failed",
                    new { newTopic.SalesforceTopicName, res });
                throw new Exception($"Failed to insert group {newTopic.SalesforceTopicName}");
            }

            // insert the RemoteGroupMapping
            var groupId = res.Data.GroupID;
            var upsertRemoteGroupMappingResponse = await AppStore.RemoteDataServiceClient
                .UpsertRemoteGroupMappingAsync(
                    new UpsertRemoteGroupMappingRequestData
                    {
                        PartyId = partyId,
                        GroupId = groupId,
                        RemoteGroupId = newTopic.SalesforceTopicId,
                        RemoteGroupName = newTopic.SalesforceTopicName
                    }, logTrace);

            logTrace.Add(LogLevel.Info, "UpsertRemoteGroupMapping() - Done"
                , new { newTopic.SalesforceTopicName, upsertRemoteGroupMappingResponse.Status });
        }

        #endregion

        #region SyncSetting methods

        /// <summary>
        /// Verify the current sync status of this Party
        /// </summary>
        /// <param name="partyId"></param>
        /// <param name="logTrace"></param>
        /// <returns>
        /// Return the SyncSetting
        /// </returns>
        /// <exception cref="IgnoreProcessingMessageException">When the sync status is not valid, should ignore</exception>
        protected async Task<GetByPartyIdResponseData> VerifySyncStatusAsync(int partyId, ILogTrace logTrace)
        {
            var title = "VerifySyncStatusAsync";
            var syncSetting = await AppStore.SyncSettingV2ServiceClient.GetByPartyIdAsync(
                new GetByPartyIdRequestData { PartyId = partyId }, logTrace);

            if (syncSetting.Status == HttpStatusCode.NotFound)
                throw new IgnoreProcessingMessageException(title, "Not set up yet");

            if (syncSetting.Status != HttpStatusCode.OK)
                throw new Exception("Fail to get SyncSetting");

            if (syncSetting.Data.IntegrationType != CRMIntegrationTypes.Salesforce
                && syncSetting.Data.IntegrationType != CRMIntegrationTypes.Practifi)
                throw new IgnoreProcessingMessageException(title, "IntegrationType is not Salesforce and Practifi");

            return syncSetting.Data;
        }

        #endregion

        #region Delete groups methods

        protected async Task DeleteGroupsAsync(
            int partyId,
            IList<SalesforceTopicMapping> salesforceTopicMappings,
            IList<SalesforceTopicMapping> remoteTopicMappings,
            IList<string> userSelectedGroupIds,
            ILogTrace logTrace)
        {
            var startedAt = DateTime.UtcNow;

            // deleted groups are the ones that currently is in the database but not in the remote list
            var deletedGroups =
                remoteTopicMappings.Except(salesforceTopicMappings, AppStore.SalesforceTopicMappingComparer);

            // if user selected to sync only some groups, also delete the groups that is not in this list
            if (userSelectedGroupIds.Any())
            {
                var userSelectedGroups = userSelectedGroupIds.Select(
                    groupId => new SalesforceTopicMapping { SalesforceTopicId = groupId }
                ).ToList();
                var unselectedGroups =
                    remoteTopicMappings.Except(userSelectedGroups, AppStore.SalesforceTopicMappingComparer);
                deletedGroups = deletedGroups.Concat(unselectedGroups).ToList();
            }

            // delete the FMG Groups first
            var groupIds = deletedGroups.Select(group => group.FMGGroupId).Distinct().ToList();
            var groupTasks = groupIds.Select(groupId => AppStore.ContactsServiceClient.DeleteGroupAsync(
                new DeleteGroupRequest { GroupID = groupId }, logTrace
            )).ToList();
            await Task.WhenAll(groupTasks);

            // delete the RemoteGroupMapping after deleting the FMG Groups because the GroupMapping objects hold the
            // FMG Group ids
            var remoteGroupIds = deletedGroups.Select(group => group.SalesforceTopicId).Distinct().ToList();
            var groupMappingTasks = remoteGroupIds.Select(remoteGroupId =>
                AppStore.RemoteDataServiceClient.DeleteRemoteGroupMappingAsync(
                    new DeleteRemoteGroupMappingRequestData
                    {
                        PartyId = partyId,
                        RemoteGroupId = remoteGroupId
                    }, logTrace
                )).ToList();
            await Task.WhenAll(groupMappingTasks);

            // log
            logTrace?.Add(LogLevel.Info, "DeleteGroupsAsync", $"{groupIds.Count()} deleted", startedAt);
        }

        #endregion

        #region Salesforce methods

        /// <summary>Gets all salesforce campaigns asynchronous.</summary>
        /// <param name="partyId">The party identifier.</param>
        /// <param name="svcSalesforce">The SVC salesforce.</param>
        /// <param name="logTrace">The log trace.</param>
        /// <returns></returns>
        protected async Task<IList<SalesforceTopicMapping>> GetAllSalesforceCampaignsAsync(
            int partyId, ISalesforceServiceClientV2 svcSalesforce, ILogTrace logTrace)
        {
            var campaigns = await SalesforceUtil.GetAllSalesforceObjectsAsync(partyId,
                () => svcSalesforce.GetAllCampaignsAsync(), svcSalesforce, AppStore.OAuthTokenManager, logTrace);

            // map the result to the SalesforceTopicMapping object
            var groupMappings = campaigns.Select(topic => new SalesforceTopicMapping
            {
                SalesforceTopicId = topic.Id,
                SalesforceTopicName = topic.Name
            });

            return groupMappings.ToList();
        }

        /// <summary>Gets all Practifi campaigns.</summary>
        /// <param name="partyId">The party identifier.</param>
        /// <param name="svcSalesforce">The SVC salesforce.</param>
        /// <param name="logTrace">The log trace.</param>
        /// <returns></returns>
        protected async Task<IList<SalesforceTopicMapping>> GetAllPractifiCampaignsAsync(
            int partyId, ISalesforceServiceClientV2 svcSalesforce, ILogTrace logTrace)
        {
            var campaigns = await SalesforceUtil.GetAllSalesforceObjectsAsync(partyId,
                () => svcSalesforce.GetAllCampaignInteractionsAsync(), svcSalesforce, AppStore.OAuthTokenManager, logTrace);

            // map the result to the SalesforceTopicMapping object
            var groupMappings = campaigns.Select(topic => new SalesforceTopicMapping
            {
                SalesforceTopicId = topic.Id,
                SalesforceTopicName = topic.Name
            });

            return groupMappings.ToList();
        }

        /// <summary>
        /// Get all Topics from Salesforce, convert to SalesforceTopicMapping schema before returning
        /// </summary>
        /// <param name="svcSalesforce"></param>
        /// <param name="logTrace"></param>
        /// <returns>
        /// The list of SalesforceTopicMapping objects representing the current Topics in Salesforce
        /// All the FMGGroupId props of these objects will be null
        /// </returns>
        protected async Task<IList<SalesforceTopicMapping>> GetAllSalesforceTopicsAsync(
            int partyId, ISalesforceServiceClientV2 svcSalesforce, ILogTrace logTrace)
        {
            var topics = await SalesforceUtil.GetAllSalesforceObjectsAsync(partyId,
                () => svcSalesforce.GetAllTopicsAsync(), svcSalesforce, AppStore.OAuthTokenManager, logTrace);

            // map the result to the SalesforceTopicMapping object
            var groupMappings = topics.Select(topic => new SalesforceTopicMapping
            {
                SalesforceTopicId = topic.Id,
                SalesforceTopicName = topic.Name
            });

            return groupMappings.ToList();
        }

        /// <summary>
        /// Init the Salesforce service client for this PartyId by getting the APIEndpoint and AccessToken from the
        /// OAuthToken service
        /// </summary>
        /// <param name="token"></param>
        /// <param name="logTrace"></param>
        /// <returns></returns>
        protected virtual ISalesforceServiceClientV2 InitSalesforceServiceClientAsync(
            OAuthTokenSyncSetting accessToken, ILogTrace logTrace)
        {
            return SalesforceServiceClientV2Factory.Create(accessToken.APIEndpoint, accessToken.AccessToken);
        }

        #endregion

        /// <summary>
        ///
        /// </summary>
        /// <param name="partyId"></param>
        /// <param name="logTrace"></param>
        /// <returns></returns>
        protected async Task<IList<SalesforceTopicMapping>> GetAllRemoteGroupMappingsAsync(
            int partyId, ILogTrace logTrace)
        {
            // send request to get all the RemoteGroupMapping
            var remoteGroupMappingRes = await AppStore.RemoteDataServiceClient.GetAllRemoteGroupMappingsAsync(
                new GetAllRemoteGroupMappingsRequestData { PartyId = partyId }, logTrace
            );

            // map to SalesforceTopicMapping schema
            var res = remoteGroupMappingRes.Data.RemoteGroupMappings.Select(groupMapping => new SalesforceTopicMapping
            {
                SalesforceTopicId = groupMapping.RemoteGroupId,
                SalesforceTopicName = groupMapping.RemoteGroupName,
                FMGGroupId = groupMapping.GroupId
            }).ToList();

            logTrace.Add(LogLevel.Info, "GetAllRemoteGroupMappingsAsync", $"# GroupMapping: {res.Count}");

            return res;
        }

        #endregion
    }
}