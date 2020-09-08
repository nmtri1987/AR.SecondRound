using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FMG.ExternalServices.Clients.SalesforceServiceV2;
using FMG.ExternalServices.Clients.SalesforceServiceV2.Models;
using FMG.Serverless.Exceptions;
using FMG.Serverless.Logging;
using FMG.Serverless.ServiceClients.OAuthTokenManager;
using FMG.Serverless.ServiceClients.OAuthTokenManager.Models;
using FMG.Serverless.Utilities;
using SalesforceOAuthError = FMG.ExternalServices.Clients.SalesforceService.Models.SalesforceOAuthError;

namespace FMG.Integrations.Salesforce.Utilities
{
    public class SalesforceUtil
    {
        #region Public methods

        /// <summary>Gets all salesforce objects asynchronous.</summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="partyId">The party identifier.</param>
        /// <param name="getFirstPageFunc">The get first page function.</param>
        /// <param name="svcSalesforce">The SVC salesforce.</param>
        /// <param name="oauthTokenManager">The oauth token manager.</param>
        /// <param name="logTrace">The log trace.</param>
        /// <returns></returns>
        public static async Task<IList<T>> GetAllSalesforceObjectsAsync<T>(int partyId,
            Func<Task<ResponseBase<SalesforceQueryResponseData<T>, IList<SalesforceError>>>> getFirstPageFunc,
            ISalesforceServiceClientV2 svcSalesforce, IOAuthTokenManager oauthTokenManager,
             ILogTrace logTrace)
        {
            var startedAt = DateTime.UtcNow;

            // send request to get the first batch from Salesforce
            var firstTopicsRes = await GetFirstSalesforceObjectPageAsync(partyId,
                getFirstPageFunc, svcSalesforce, oauthTokenManager, logTrace);
            var sfObjects = firstTopicsRes.Data.Records;

            // repeat until all the topics are retrieved, just in case, usually there won't be more than 1000 topics
            var nextRecordUrl = firstTopicsRes.Data.NextRecordsUrl;
            while (nextRecordUrl != null)
            {
                var nextTopicsRes = await svcSalesforce.GetNextPageAsync(firstTopicsRes.Data);
                nextRecordUrl = nextTopicsRes.Data.NextRecordsUrl;
                sfObjects = sfObjects.Concat(nextTopicsRes.Data.Records).ToList();
            }

            logTrace?.Add(LogLevel.Info, $"GetAllSalesforce{typeof(T).Name}Async() - Done", $"{typeof(T).Name}s #{sfObjects.Count}", startedAt);

            return sfObjects.ToList();
        } 

        #endregion

        #region Private methods

        /// <summary>
        /// Get the first Topic page from Salesforce
        /// If the AccessToken is expired, auto refresh the AccessToken and assign the updated AccessToken to the
        /// svcSalesforce instance passed into the method
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="partyId">The party identifier.</param>
        /// <param name="getFirstPageFunc">The get first page function.</param>
        /// <param name="svcSalesforce">The SVC salesforce.</param>
        /// <param name="oauthTokenManager">The oauth token manager.</param>
        /// <param name="logTrace">The log trace.</param>
        /// <returns></returns>
        /// <exception cref="Exception">
        /// </exception>
        /// <exception cref="IgnoreProcessingMessageException">Refresh Token Failed. - Failed to RefreshAccessToken for partyId = {partyId}</exception>
        private static async Task<ResponseBase<SalesforceQueryResponseData<T>, IList<SalesforceError>>>
            GetFirstSalesforceObjectPageAsync<T>(int partyId,
            Func<Task<ResponseBase<SalesforceQueryResponseData<T>, IList<SalesforceError>>>> getFirstPageFunc,
            ISalesforceServiceClientV2 svcSalesforce, IOAuthTokenManager oauthTokenManager,
             ILogTrace logTrace)
        {
            var domain = $"GetFirstSalesforce{typeof(T).Name}Page";
            var startedAt = DateTime.UtcNow;

            // send request to get the first batch from Salesforce
            var res = await getFirstPageFunc();
            logTrace?.Add(LogLevel.Info, domain, "First try", startedAt);

            // success case
            if (res.Status == HttpStatusCode.OK) return res;

            // error case
            var errorCode = res.Error?.FirstOrDefault()?.ErrorCode;

            if (errorCode == null)
                throw new Exception($"{domain} - Status {res.Status} - Message {res.ErrorString}");

            if (errorCode != "INVALID_SESSION_ID")
                throw new Exception(
                    $"{domain} - Status {res.Status} - Code {errorCode} - Message {res.ErrorString}"
                );

            logTrace?.Add(LogLevel.Info, domain, "AccessToken expired");

            // try to refresh token
            startedAt = DateTime.UtcNow;
            var oauthToken = await oauthTokenManager.RefreshAccessTokenAsync<SalesforceOAuthError>(
                new RefreshAccessTokenRequest
                {
                    PartyId = partyId,
                    MaxRetries = 5,
                    RetryWaitSecondsIfFailed = 5
                }, logTrace);

            if (oauthToken == null)
            {
                logTrace?.Add(LogLevel.Warning, domain, "Refresh Token Failed.", startedAt);
                throw new IgnoreProcessingMessageException("Refresh Token Failed.", $"Failed to RefreshAccessToken for partyId = {partyId}");
            }

            logTrace?.Add(LogLevel.Info, domain, "AccessToken refreshed", startedAt);

            // update the access token in svcSalesforce
            svcSalesforce.AccessToken = oauthToken.AccessToken;

            // try again
            startedAt = DateTime.UtcNow;
            res = await getFirstPageFunc();
            logTrace?.Add(LogLevel.Info, domain, "Task retried", startedAt);

            // success case
            if (res.Status == HttpStatusCode.OK) return res;

            // error case
            throw new Exception($"{domain} - Status {res.Status} - Code {errorCode} - Message {res.ErrorString}");
        } 

        #endregion


    }
}
