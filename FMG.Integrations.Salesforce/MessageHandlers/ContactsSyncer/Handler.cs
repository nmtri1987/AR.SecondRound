using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FMG.ExternalServices.Clients.SalesforceServiceV2;
using FMG.ExternalServices.Clients.SalesforceServiceV2.Models;
using FMG.Integrations.Salesforce.Utilities;
using FMG.Serverless.Exceptions;
using FMG.Serverless.Logging;
using FMG.Serverless.MessageHandlers;
using FMG.Serverless.ServiceClients.OAuthTokenManager.Models;
using FMG.Serverless.ServiceClients.OAuthTokenV2.Models;
using FMG.Serverless.ServiceClients.RemoteData.Models;
using FMG.Serverless.ServiceClients.SyncSettingV2.Models;
using FMG.Serverless.Utilities;
using FMG.Serverless.Utilities.Constants;
using FMG.Serverless.Utilities.Helpers.Tasks;
using FMG.ServiceBus;
using FMG.ServiceBus.MessageModels;
using SalesforceError = FMG.ExternalServices.Clients.SalesforceServiceV2.Models.SalesforceError;
using SalesforceOAuthError = FMG.ExternalServices.Clients.SalesforceService.Models.SalesforceOAuthError;

namespace FMG.Integrations.Salesforce.MessageHandlers.ContactsSyncer
{
    public class Handler : MessageHandler<SalesforceContactsSyncerMessage, AppStore>
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="appStore"></param>
        public Handler(AppStore appStore) : base(appStore)
        {
        }

        #region Implement IMessageHandler

        /// <summary>
        /// Processes the async.
        /// </summary>
        /// <returns>The async.</returns>
        /// <param name="message">Message.</param>
        /// <param name="logTrace">Log trace.</param>
        public override async Task<IEnumerable<IMessage>> ProcessAsync
            (SalesforceContactsSyncerMessage message, ILogTrace logTrace)
        {
            var partyId = message.PartyId;

            // get the sync setting first
            var syncSetting = await GetSyncSettingAsync(partyId, logTrace);

            // init the salesforce client
            var svcSalesforce = InitSalesforceServiceClientAsync(syncSetting.OAuthToken, logTrace);
            var getContactsData = new SalesforceQueryResponseData<Contact>();
            var isFullSync = !(syncSetting.RemoteGroupIDs?.Count > 0);

            // get remote contact ids belong at least 1 group.
            // remoteContactIdsBelongGroup is empty that mean FULL SYNC
            var remoteContactIdsBelongGroup = new Dictionary<string, bool>();
            if (!isFullSync)
            {
                remoteContactIdsBelongGroup = await GetRemoteContactIdsBelongGroupAsync(partyId, logTrace);
            }

            // 1. Get contacts from Salesforce
            // 2. Publish each contact pages for FMGContactUpdater
            do
            {
                // get first page, write total contact to Redis.
                if (string.IsNullOrWhiteSpace(getContactsData.NextRecordsUrl))
                {
                    getContactsData = await GetContacts(partyId, svcSalesforce, syncSetting, logTrace);

                    // write total contact to redis.
                    await WriteTotalContactToRedis(partyId, getContactsData.TotalSize, logTrace);
                }
                else
                {
                    getContactsData = await GetContactsNextPage(svcSalesforce, getContactsData, logTrace);
                }

                await PublishToFmgContactUpdater(syncSetting, getContactsData?.Records, remoteContactIdsBelongGroup,
                    isFullSync, logTrace);
            } while (!string.IsNullOrWhiteSpace(getContactsData.NextRecordsUrl));

            return null;
        }

        #endregion


        #region Internal Helpers

        /// <summary>
        /// Writes the total contact to redis.
        /// This work is used to prepare for update sync status when sync process finished.
        /// </summary>
        /// <param name="partyId">The party identifier.</param>
        /// <param name="totalContact">The total contact.</param>
        /// <param name="logTrace">The log trace.</param>
        async Task WriteTotalContactToRedis(int partyId, int totalContact, ILogTrace logTrace)
        {
            ...
        }

        /// <summary>
        /// Get SyncSetting by PartyId
        /// </summary>
        /// <param name="partyId"></param>
        /// <param name="logTrace"></param>
        /// <returns>The SyncSetting</returns>
        /// <exception cref="IgnoreProcessingMessageException">When the sync status is not valid, should ignore</exception>
        /// <exception cref="Exception"></exception>
        async Task<SyncSetting> GetSyncSettingAsync(int partyId, ILogTrace logTrace)
        {
            ...
        }

        static readonly object _lock = new object();

        /// <summary>
        /// 1. Convert contacts from Salesforce to FMGContacts
        /// 2. Publish FMGContacts to FMGContactUpdater
        /// </summary>
        /// <param name="partyId"></param>
        /// <param name="contacts"></param>
        /// <param name="logTrace"></param>
        /// <returns></returns>
        async Task PublishToFmgContactUpdater(SyncSetting syncSetting, IList<Contact> contacts,
            IDictionary<string, bool> remoteContactIdsBelongGroup, bool isFullSync, ILogTrace logTrace)
        {
            ...
        }

        /// <summary>
        /// Convert Contact to FMGContactUpdaterMessage and publish it.
        /// </summary>
        /// <param name="partyId">The party identifier.</param>
        /// <param name="contact">The contact.</param>
        /// <param name="logTrace">The log trace.</param>
        async Task PublishContactToUpdate(SyncSetting syncSetting, Contact contact,
            IDictionary<string, bool> remoteContactIdsBelongGroup,
            bool isFullSync, ILogTrace logTrace = null)
        {
            ...
        }

        /// <summary>
        /// Get first page for Contacts from Salesforce
        /// </summary>
        /// <param name="partyId"></param>
        /// <param name="svcSalesforce"></param>
        /// <param name="syncSetting"></param>
        /// <param name="logTrace"></param>
        /// <returns></returns>
        async Task<SalesforceQueryResponseData<Contact>> GetContacts(
            int partyId, ISalesforceServiceClientV2 svcSalesforce, SyncSetting syncSetting, ILogTrace logTrace)
        {
            var domain = "GetContacts()";
            Func<Task<ResponseBase<SalesforceQueryResponseData<Contact>, IList<SalesforceError>>>> getContactsTask;
            Func<Task<ResponseBase<SalesforceQueryResponseData<Contact>, IList<SalesforceError>>>>
                getContactsFallbackLevel1Task;
            Func<Task<ResponseBase<SalesforceQueryResponseData<Contact>, IList<SalesforceError>>>>
                getContactsFallbackLevel2Task;
            var startedAt = DateTime.UtcNow;

            var previousSyncStartDate = syncSetting.PreviousSyncStartDate;
            logTrace?.Add(LogLevel.Info, domain, $"PreviousSyncStartDate: {previousSyncStartDate}");

            // full sync for delta sync
            var isFullSync = AppStore.Config.ForceFullSync || !previousSyncStartDate.HasValue;
            logTrace?.Add(LogLevel.Info, domain, $"IsFullSync: {isFullSync}");

            // the time to use to query the updated contacts
            DateTime updatedSinceTime;

            // Sync all
            // NOTE: If you update the logic here, refer to the logic below for getting contacts with fallback fields
            // the 2 logic are a bit duplicated

            // Full or Delta sync
            if (!isFullSync)
            {
                updatedSinceTime = previousSyncStartDate.Value.Subtract(TimeSpan.FromHours(24));

                getContactsTask = () => svcSalesforce.GetAllContactsAsync(updatedSinceTime, true);
                getContactsFallbackLevel1Task = () => svcSalesforce.GetAllContactsAsync(
                    updatedSinceTime, Contact.FallbackBirthdateFields, true
                );
                getContactsFallbackLevel2Task = () => svcSalesforce.GetAllContactsAsync(
                    updatedSinceTime, Contact.FallbackIntegrationFields, true
                );
            }
            else
            {
                getContactsTask = () => svcSalesforce.GetAllContactsAsync(true);
                getContactsFallbackLevel1Task = () => svcSalesforce.GetAllContactsAsync(
                    Contact.FallbackBirthdateFields, true
                );
                getContactsFallbackLevel2Task = () => svcSalesforce.GetAllContactsAsync(
                    Contact.FallbackIntegrationFields, true
                );
            }

            // try getting all contacts with the default fields first
            var response = await GetContactsWithOutOfSyncHandler(partyId, svcSalesforce, getContactsTask, logTrace);
            logTrace?.Add(LogLevel.Info, domain, new { response?.Status }, startedAt);
            if (response != null) return response.Data;

            // No need to wrap OutOfSync handler here because if the AccessToken expired,
            // it was handled by the above code already

            // try fallback to other fields
            logTrace?.Add(LogLevel.Warning, domain, "Retrying with fallback birthdate fields");
            response = await getContactsFallbackLevel1Task();
            if (response.Status == HttpStatusCode.OK) return response.Data;

            // try fallback to other fields
            logTrace?.Add(LogLevel.Warning, domain, "Retrying with fallback fields");
            response = await getContactsFallbackLevel2Task();
            if (response.Status == HttpStatusCode.OK) return response.Data;

            logTrace?.Add(LogLevel.Error, $"{domain} - Failed", new { response });
            throw new Exception($"Failed to get contacts from Salesforce. " +
                                $"Status: {response.Status}, Error: {response.ErrorString}");
        }

        /// <summary>
        /// Get First page of Contacts from Salesforce with Out Of Sync Handling
        /// - Try to send request to Salesforce to get the first page of Contacts
        /// - If success, return the data
        /// - If error but not Token expired error, return null
        /// - If Token expired error, try refreshing the Access Token
        /// - Retry the request again
        /// - If success, return the data
        /// - Otherwise, return null
        /// </summary>
        /// <param name="partyId"></param>
        /// <param name="svcSalesforce"></param>
        /// <param name="buildTask">
        /// A function to build the request task that send the request to Salesforce
        /// This function has to use the same instance of svcSalesforce passed into this method so when this
        /// method refresh token, it can assign back to the svcSalesforce instance
        /// </param>
        /// <param name="logTrace"></param>
        /// <returns>
        /// Return the data if success
        /// Return null for error
        /// </returns>
        protected async Task<ResponseBase<SalesforceQueryResponseData<Contact>, IList<SalesforceError>>>
            GetContactsWithOutOfSyncHandler(int partyId,
                ISalesforceServiceClientV2 svcSalesforce,
                Func<Task<ResponseBase<SalesforceQueryResponseData<Contact>, IList<SalesforceError>>>> buildTask,
                ILogTrace logTrace)
        {
            var domain = "GetContactsWithOutOfSyncHandler";
            var startedAt = DateTime.UtcNow;

            // send request to get the first batch from Salesforce
            var res = await buildTask();
            logTrace?.Add(LogLevel.Info, domain, "First try", startedAt);

            // success case
            if (res.Status == HttpStatusCode.OK) return res;

            // error case
            var errorCode = res.Error?.FirstOrDefault()?.ErrorCode;
            if (errorCode == null) return null;
            if (errorCode != "INVALID_SESSION_ID") return null;

            logTrace?.Add(LogLevel.Info, domain, "AccessToken expired");

            // try to refresh token
            startedAt = DateTime.UtcNow;
            var oauthToken = await AppStore.OAuthTokenManager.RefreshAccessTokenAsync<SalesforceOAuthError>(
                new RefreshAccessTokenRequest
                {
                    PartyId = partyId,
                    MaxRetries = 5,
                    RetryWaitSecondsIfFailed = 5
                }, logTrace);

            if (oauthToken == null)
            {
                logTrace?.Add(LogLevel.Warning, domain, "Refresh Token Failed.", startedAt);
                throw new IgnoreProcessingMessageException("Refresh Token Failed.",
                    $"Failed to RefreshAccessToken for partyId = {partyId}");
            }

            logTrace?.Add(LogLevel.Info, domain, "AccessToken refreshed", startedAt);

            // update the access token in svcSalesforce
            svcSalesforce.AccessToken = oauthToken.AccessToken;

            // try again
            startedAt = DateTime.UtcNow;
            res = await buildTask();
            logTrace?.Add(LogLevel.Info, domain, "Task retried", startedAt);

            // success case
            if (res.Status == HttpStatusCode.OK) return res;

            return null;
        }

        /// <summary>
        /// Get next page for Contacts from Salesforce
        /// </summary>
        /// <param name="svcSalesforce"></param>
        /// <param name="previousPage"></param>
        /// <param name="logTrace"></param>
        /// <returns></returns>
        async Task<SalesforceQueryResponseData<Contact>>
            GetContactsNextPage(ISalesforceServiceClientV2 svcSalesforce
                , SalesforceQueryResponseData<Contact> previousPage
                , ILogTrace logTrace)
        {
            var domain = "GetContactsNextPage";
            var startedAt = DateTime.UtcNow;

            var response = await svcSalesforce.GetNextPageAsync(previousPage);

            logTrace?.Add(LogLevel.Info, $"{domain} - Done. #{response.Data?.Records?.Count}"
                , new { response.Status }, startedAt);

            if (response.Status != HttpStatusCode.OK)
            {
                logTrace?.Add(LogLevel.Error, $"{domain} - Failed", new { response });
                throw new Exception($"Failed to get contacts from Salesforce. " +
                                    $"Status: {response.Status}");
            }

            return response.Data;
        }

        /// <summary>
        /// 1. Get AccessToken from OAuthToken
        /// 2. Create SalesforceService by use AccessToken
        /// </summary>
        /// <param name="message"></param>
        /// <param name="logTrace"></param>
        /// <returns></returns>
        protected virtual ISalesforceServiceClientV2 InitSalesforceServiceClientAsync(
            OAuthTokenSyncSetting accessToken, ILogTrace logTrace)
        {
            return SalesforceServiceClientV2Factory.Create(accessToken.APIEndpoint
                , accessToken.AccessToken);
        }

        /// <summary>Gets the remote contact ids belong group.</summary>
        /// <param name="partyId">The party identifier.</param>
        /// <param name="logTrace">The log trace.</param>
        /// <returns></returns>
        async Task<Dictionary<string, bool>> GetRemoteContactIdsBelongGroupAsync(int partyId, ILogTrace logTrace)
        {
            ...
        }

        #endregion
    }
}