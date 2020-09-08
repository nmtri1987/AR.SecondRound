# Overview

FMGSuite is an Automation Marketing platform for Finacial Advisors in the US. Many financial advisors
have already used another CRM system to manage their customer information. In order for them to
enter FMGSuite, we need an integration system to synchronize the customer list from the third party
system to our FMG system. Let's start with Salesforce, one of the most popular CRM system on the
market now.

There are 2 main entities that we need to sync from the
- Groups (Topics in Salesforce)
- Contacts (Customers)

We chose to write it in Lambda to reduce the maintenance cost and make use of the auto-scaling
feature. This repo contains a Lambda code of the above 2 workers.

Your task is to review and detect any problems with the code. You don't have to detect any problems
with the business logic or the typo because this is just the sample code. You are also expected to
give the solution to for those problems.

# Instructions

- Clone this repo to your local computer
- Push this repo to your personal Github account
- Review the code and detect any problems
- For each problem, create a Github issue on your cloned repo and describe the details, including
  - Place in code
  - Problem
  - Solution
  - Other description if necessary

# Notes

Each worker subscribes to an AWS SQS queue. We don't need to handle any logic related to pulling or
acknowledging message. AWS Lambda handles it automatically. The Lambda code simply processes the
input message. The message will be considered success if there is no error thrown. The main handler
is located in the `Handler` class. We also included some base classes for your reference.

Assume that these interfaces and classes have been already implemented in another Nuget package

- SalesforceOAuthServiceClient
- IOAuthTokenManager
- IContactsServiceClient
- IRemoteDataServiceClient
- IOAuthTokenV2ServiceClient
- ISyncSettingV2ServiceClient
- ILogTrace

This is not fully working code! If there is anything you need clarification, feel free to ask.
