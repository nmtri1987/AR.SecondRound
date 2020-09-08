using System;
using System.Linq;
using FMG.ExternalServices.Clients.SalesforceServiceV2.Models;
using FMG.Serverless.Utilities;
using FMG.ServiceBus.MessageModels;

namespace FMG.Integrations.Salesforce.Utilities
{
    public class MapperUtil
    {
        public static FMGContact MapContact(Contact salesforceContact)
        {
            var contact = new FMGContact
            {
                FirstName = StringHelpers.Truncate(salesforceContact.FirstName, 50),
                LastName = StringHelpers.Truncate(salesforceContact.LastName, 50),
                EmailAddress = StringHelpers.Truncate(salesforceContact.Email, 250),
                Phone = StringHelpers.Truncate(salesforceContact.Phone, 20),
                BirthDate = salesforceContact.Birthdate ?? salesforceContact.BirthDateC
            };

            if (!string.IsNullOrWhiteSpace(salesforceContact.MailingStreet) ||
                !string.IsNullOrWhiteSpace(salesforceContact.MailingCity) ||
                !string.IsNullOrWhiteSpace(salesforceContact.MailingState) ||
                !string.IsNullOrWhiteSpace(salesforceContact.MailingPostalCode))
            {
                contact.Address = new FMGAddress
                {
                    Address1 = StringHelpers.Truncate(salesforceContact.MailingStreet, 100),
                    Address2 = "",
                    City = StringHelpers.Truncate(salesforceContact.MailingCity, 100),
                    State = AddressHelpers.GetStateAbbreviation(StringHelpers.Truncate(salesforceContact.MailingState, 100)),
                    PostalCode = StringHelpers.Truncate(salesforceContact.MailingPostalCode, 20)
                };
            }

            return contact;
        }
    }
}
