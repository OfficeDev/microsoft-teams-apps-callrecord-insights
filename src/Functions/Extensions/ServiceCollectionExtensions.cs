using Azure.Core;
using Azure.Identity;
using CallRecordInsights.Flattener;
using CallRecordInsights.Models;
using CallRecordInsights.Services;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Identity.Abstractions;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Microsoft.Kiota.Abstractions.Authentication;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CallRecordInsights.Extensions
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds the <see cref="JsonFlattener"/> with the default configuration for <see cref="KustoCallRecord"/> to the service collection.
        /// </summary>
        /// <param name="services"></param>
        /// <returns></returns>
        public static IServiceCollection AddJsonToKustoFlattener(this IServiceCollection services)
        {
            services.AddSingleton(serviceProvider => IKustoCallRecordHelpers.DefaultConfiguration);
            services.AddSingleton<IJsonProcessor, JsonFlattener>();
            return services;
        }

        /// <summary>
        /// Adds the <see cref="CallRecordsGraphContext"/> to the service collection.
        /// </summary>
        /// <param name="services"></param>
        /// <param name="sectionName"></param>
        /// <returns></returns>
        public static IServiceCollection AddCallRecordsGraphContext(
            this IServiceCollection services,
            string sectionName = "GraphSubscription")
        {
            return services
                .AddScoped(serviceProvider => new CallRecordsGraphOptions(
                               serviceProvider.GetRequiredService<IConfiguration>().GetSection(sectionName)))
                .AddMicrosoftGraphAsApplication()
                .AddScoped<ICallRecordsGraphContext,CallRecordsGraphContext>();
        }

        /// <summary>
        /// Adds the <see cref="CosmosClient"/> to the service collection.
        /// </summary>
        /// <param name="services"></param>
        /// <param name="sectionName"></param>
        /// <returns></returns>
        public static IServiceCollection AddCallRecordsDataContext(
            this IServiceCollection services,
            string sectionName = "CallRecordInsightsDb")
        {
            return services
                .AddSingleton<ICallRecordsDataContext, CallRecordsDataContext>(serviceProvider =>
                    new CallRecordsDataContext(
                        new DefaultAzureCredential(),
                        serviceProvider.GetRequiredService<IConfiguration>().GetSection(sectionName),
                        serviceProvider.GetRequiredService<ILogger<CallRecordsDataContext>>())
                );
        }

        /// <summary>
        /// Adds the <see cref="GraphServiceClient"/> to the service collection using the <see cref="AzureIdentityMultiTenantGraphAuthenticationProvider"/>.
        /// </summary>
        /// <param name="services"></param>
        /// <param name="sectionName"></param>
        /// <returns></returns>
        public static IServiceCollection AddMicrosoftGraphAsApplication(
            this IServiceCollection services,
            string sectionName = "AzureAd")
        {
            return services
                .AddTokenCredential(sectionName)
                .AddAzureMultiTenantGraphAuthenticationProvider()
                .AddScoped(sp =>
                    new GraphServiceClient(
                        authenticationProvider: sp.GetRequiredService<IAuthenticationProvider>(),
                        baseUrl: $"https://{sp.GetRequiredService<CallRecordsGraphOptions>().Endpoint}/v1.0"
                    ));
        }

        /// <summary>
        /// Adds the <see cref="AzureIdentityMultiTenantGraphAuthenticationProvider"/> to the service collection.
        /// </summary>
        /// <param name="services"></param>
        /// <returns></returns>
        public static IServiceCollection AddAzureMultiTenantGraphAuthenticationProvider(this IServiceCollection services)
        {
            return services
                .AddScoped<IAuthenticationProvider, AzureIdentityMultiTenantGraphAuthenticationProvider>();
        }

        /// <summary>
        /// Adds the <see cref="TokenCredential"/> to the service collection using the <see cref="DefaultAzureCredential"/> 
        /// and <see cref="ClientCertificateCredential"/> for the <see cref="MicrosoftIdentityOptions.ClientCredentials"/> 
        /// and <see cref="ClientSecretCredential"/> for the <see cref="MicrosoftIdentityOptions.ClientCertificates"/>.
        /// </summary>
        /// <param name="services"></param>
        /// <param name="sectionName"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public static IServiceCollection AddTokenCredential(this IServiceCollection services, string sectionName)
        {
            var identityOptions = services.BuildServiceProvider()
                .GetRequiredService<IConfiguration>()
                .GetSection(sectionName)
                .Get<MicrosoftIdentityOptions>();
            var tokenCredentials = new List<TokenCredential>();

            var defaultAzureCredentialOptions = new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = identityOptions?.UserAssignedManagedIdentityClientId,
                TenantId = identityOptions?.TenantId,
            };

            if (identityOptions?.Authority != null)
                defaultAzureCredentialOptions.AuthorityHost = new Uri(identityOptions.Authority);

            defaultAzureCredentialOptions.AdditionallyAllowedTenants.Add("*");

            var clientCertificateCredentialOptions = new ClientCertificateCredentialOptions();
            clientCertificateCredentialOptions.AdditionallyAllowedTenants.Add("*");

            var clientSecretCredentialOptions = new ClientSecretCredentialOptions();
            clientSecretCredentialOptions.AdditionallyAllowedTenants.Add("*");

            var clientAssertionCredentialOptions = new ClientAssertionCredentialOptions();
            clientAssertionCredentialOptions.AdditionallyAllowedTenants.Add("*");

#if DEBUG
            defaultAzureCredentialOptions.Diagnostics.IsLoggingEnabled = true;
            defaultAzureCredentialOptions.Diagnostics.IsAccountIdentifierLoggingEnabled = true;

            clientCertificateCredentialOptions.Diagnostics.IsLoggingEnabled = true;
            clientCertificateCredentialOptions.Diagnostics.IsAccountIdentifierLoggingEnabled = true;

            clientSecretCredentialOptions.Diagnostics.IsLoggingEnabled = true;
            clientSecretCredentialOptions.Diagnostics.IsAccountIdentifierLoggingEnabled = true;

            clientAssertionCredentialOptions.Diagnostics.IsLoggingEnabled = true;
            clientAssertionCredentialOptions.Diagnostics.IsAccountIdentifierLoggingEnabled = true;
#endif

            DefaultCertificateLoader.UserAssignedManagedIdentityClientId = identityOptions?.UserAssignedManagedIdentityClientId;

            if (identityOptions?.ClientCredentials != null)
            {
                var credLoader = new DefaultCredentialsLoader();
                tokenCredentials.AddRange(identityOptions.ClientCredentials
                    .Where(c => credLoader.ShouldAddTokenCredential(c))
                    .Select<CredentialDescription, TokenCredential>(c =>
                        c.CredentialType switch
                        {
                            CredentialType.Certificate =>
                                new ClientCertificateCredential(identityOptions.TenantId, identityOptions.ClientId, c.Certificate, clientCertificateCredentialOptions),
                            CredentialType.Secret =>
                                new ClientSecretCredential(identityOptions.TenantId, identityOptions.ClientId, c.ClientSecret, clientSecretCredentialOptions),
                            CredentialType.SignedAssertion =>
                                c.SourceType switch
                                {
                                    CredentialSource.SignedAssertionFromManagedIdentity =>
                                        new ClientAssertionCredential(
                                            identityOptions.TenantId,
                                            identityOptions.ClientId,
                                            async cancellationToken =>
                                            {
                                                var assertion = c.CachedValue as ManagedIdentityClientAssertion;
                                                var options = new AssertionRequestOptions()
                                                {
                                                    CancellationToken = cancellationToken,
                                                };
                                                return await assertion.GetSignedAssertionAsync(options);
                                            },
                                            clientAssertionCredentialOptions),
                                    CredentialSource.SignedAssertionFilePath =>
                                        new ClientAssertionCredential(
                                            identityOptions.TenantId,
                                            identityOptions.ClientId,
                                            async cancellationToken =>
                                            {
                                                var assertion = c.CachedValue as AzureIdentityForKubernetesClientAssertion;
                                                var options = new AssertionRequestOptions()
                                                {
                                                    CancellationToken = cancellationToken,
                                                };
                                                return await assertion.GetSignedAssertionAsync(options);
                                            },
                                            clientAssertionCredentialOptions),
                                    CredentialSource.SignedAssertionFromVault =>
                                        new ClientAssertionCredential(
                                            identityOptions.TenantId,
                                            identityOptions.ClientId,
                                            async cancellationToken =>
                                            {
                                                var assertion = c.CachedValue as ClientAssertionProviderBase;
                                                var options = new AssertionRequestOptions()
                                                {
                                                    CancellationToken = cancellationToken,
                                                };
                                                return await assertion.GetSignedAssertionAsync(options);
                                            },
                                            clientAssertionCredentialOptions),
                                    _ => throw new NotImplementedException(),
                                },
                            _ => throw new NotImplementedException(),
                        }));
            }

            if (identityOptions?.ClientCertificates != null)
            {
                var credLoader = new DefaultCertificateLoader();
                tokenCredentials.AddRange(identityOptions.ClientCertificates
                    .Where(c => credLoader.ShouldAddTokenCredential(c))
                    .Select(c => new ClientCertificateCredential(identityOptions.TenantId, identityOptions.ClientId, c.Certificate, clientCertificateCredentialOptions)));
            }

            tokenCredentials.Add(new DefaultAzureCredential(defaultAzureCredentialOptions));

            return services.AddScoped<TokenCredential, ChainedTokenCredential>(_ => new ChainedTokenCredential(tokenCredentials.ToArray()));
        }

        internal static bool ShouldAddTokenCredential(this DefaultCredentialsLoader credentialsLoader, CredentialDescription credentialDescription)
        {
            if (credentialDescription.Skip || credentialDescription.CredentialType == CredentialType.DecryptKeys) return false;
            try
            {
                credentialsLoader.LoadCredentialsIfNeededAsync(credentialDescription).GetAwaiter().GetResult();
            }
            catch (Exception _)
            when (_ is ArgumentException
                || _ is NotSupportedException
                || _ is InvalidOperationException
                || _ is System.Security.Cryptography.CryptographicException
                || _ is System.IO.IOException)
            {
            }
            if (credentialDescription.Skip) return false;
            if (credentialDescription.CredentialType == CredentialType.Secret)
                return !string.IsNullOrEmpty(credentialDescription.ClientSecret);

            return credentialDescription.CachedValue is not null;
        }
    }
}
