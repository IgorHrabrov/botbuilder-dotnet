﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Bot.Builder.BotFramework;
using Microsoft.Bot.Builder.Streaming;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.Bot.Builder.Integration.AspNet.Core
{
    /// <summary>
    /// A Bot Builder Adapter implementation used to handled bot Framework HTTP requests.
    /// </summary>
    public class BotFrameworkHttpAdapter : BotFrameworkHttpAdapterBase, IBotFrameworkHttpAdapter
    {
        private const string AuthHeaderName = "authorization";
        private const string ChannelIdHeaderName = "channelid";

        public BotFrameworkHttpAdapter(ICredentialProvider credentialProvider = null, IChannelProvider channelProvider = null, ILogger<BotFrameworkHttpAdapter> logger = null)
            : base(credentialProvider ?? new SimpleCredentialProvider(), channelProvider, logger)
        {
        }

        public BotFrameworkHttpAdapter(ICredentialProvider credentialProvider, IChannelProvider channelProvider, HttpClient httpClient, ILogger<BotFrameworkHttpAdapter> logger)
            : base(credentialProvider ?? new SimpleCredentialProvider(), channelProvider, httpClient, logger)
        {
        }

        protected BotFrameworkHttpAdapter(IConfiguration configuration, ILogger<BotFrameworkHttpAdapter> logger = null)
            : base(new ConfigurationCredentialProvider(configuration), new ConfigurationChannelProvider(configuration), logger: logger)
        {
            var openIdEndpoint = configuration.GetSection(AuthenticationConstants.BotOpenIdMetadataKey)?.Value;

            if (!string.IsNullOrEmpty(openIdEndpoint))
            {
                // Indicate which Cloud we are using, for example, Public or Sovereign.
                ChannelValidation.OpenIdMetadataUrl = openIdEndpoint;
                GovernmentChannelValidation.OpenIdMetadataUrl = openIdEndpoint;
            }
        }

        public async Task ProcessAsync(HttpRequest httpRequest, HttpResponse httpResponse, IBot bot, CancellationToken cancellationToken = default)
        {
            if (httpRequest == null)
            {
                throw new ArgumentNullException(nameof(httpRequest));
            }

            if (httpResponse == null)
            {
                throw new ArgumentNullException(nameof(httpResponse));
            }

            if (bot == null)
            {
                throw new ArgumentNullException(nameof(bot));
            }

            if (httpRequest.Method == HttpMethods.Get)
            {
                await ConnectWebSocketAsync(bot, httpRequest, httpResponse).ConfigureAwait(false);
            }
            else
            {
                // Deserialize the incoming Activity
                var activity = await HttpHelper.ReadRequestAsync(httpRequest).ConfigureAwait(false);

                if (string.IsNullOrEmpty(activity?.Type))
                {
                    httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                    return;
                }

                // Grab the auth header from the inbound http request
                var authHeader = httpRequest.Headers["Authorization"];

                try
                {
                    // Process the inbound activity with the bot
                    var invokeResponse = await ProcessActivityAsync(authHeader, activity, bot.OnTurnAsync, cancellationToken).ConfigureAwait(false);

                    // write the response, potentially serializing the InvokeResponse
                    await HttpHelper.WriteResponseAsync(httpResponse, invokeResponse).ConfigureAwait(false);
                }
                catch (UnauthorizedAccessException)
                {
                    // handle unauthorized here as this layer creates the http response
                    httpResponse.StatusCode = (int)HttpStatusCode.Unauthorized;
                }
            }
        }

        /// <summary>
        /// Process the initial request to establish a long lived connection via a streaming server.
        /// </summary>
        /// <param name="bot">The <see cref="IBot"/> instance.</param>
        /// <param name="httpRequest">The connection request.</param>
        /// <param name="httpResponse">The response sent on error or connection termination.</param>
        /// <returns>Returns on task completion.</returns>
        private async Task ConnectWebSocketAsync(IBot bot, HttpRequest httpRequest, HttpResponse httpResponse)
        {
            if (httpRequest == null)
            {
                throw new ArgumentNullException(nameof(httpRequest));
            }

            if (httpResponse == null)
            {
                throw new ArgumentNullException(nameof(httpResponse));
            }

            ConnectedBot = bot ?? throw new ArgumentNullException(nameof(bot));

            if (!httpRequest.HttpContext.WebSockets.IsWebSocketRequest)
            {
                httpRequest.HttpContext.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                await httpRequest.HttpContext.Response.WriteAsync("Upgrade to WebSocket is required.").ConfigureAwait(false);

                return;
            }

            if (!await AuthenticateRequestAsync(httpRequest).ConfigureAwait(false))
            {
                httpRequest.HttpContext.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                await httpRequest.HttpContext.Response.WriteAsync("Request authentication failed.").ConfigureAwait(false);

                return;
            }

            try
            {
                var socket = await httpRequest.HttpContext.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
                
                var requestHandler = new StreamingRequestHandler(bot, this, socket, Logger);

                if (RequestHandlers == null)
                {
                    RequestHandlers = new List<StreamingRequestHandler>();
                }

                RequestHandlers.Add(requestHandler);

                await requestHandler.ListenAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                httpRequest.HttpContext.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                await httpRequest.HttpContext.Response.WriteAsync($"Unable to create transport server. Error: {ex.ToString()}").ConfigureAwait(false);

                throw ex;
            }
        }

        private async Task<bool> AuthenticateRequestAsync(HttpRequest httpRequest)
        {
            try
            {
                if (!await CredentialProvider.IsAuthenticationDisabledAsync().ConfigureAwait(false))
                {
                    var authHeader = httpRequest.Headers.First(x => x.Key.ToLower() == AuthHeaderName).Value.FirstOrDefault();
                    var channelId = httpRequest.Headers.First(x => x.Key.ToLower() == ChannelIdHeaderName).Value.FirstOrDefault();

                    if (string.IsNullOrWhiteSpace(authHeader))
                    {
                        await WriteUnauthorizedResponseAsync(AuthHeaderName, httpRequest).ConfigureAwait(false);
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(channelId))
                    {
                        await WriteUnauthorizedResponseAsync(ChannelIdHeaderName, httpRequest).ConfigureAwait(false);
                        return false;
                    }

                    var claimsIdentity = await JwtTokenValidation.ValidateAuthHeader(authHeader, CredentialProvider, ChannelProvider, channelId).ConfigureAwait(false);
                    if (!claimsIdentity.IsAuthenticated)
                    {
                        httpRequest.HttpContext.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                        return false;
                    }
                    
                    // Add ServiceURL to the cache of trusted sites in order to allow token refreshing.
                    AppCredentials.TrustServiceUrl(claimsIdentity.FindFirst(AuthenticationConstants.ServiceUrlClaim).Value);
                    ClaimsIdentity = claimsIdentity;
                }

                return true;
            }
            catch (Exception ex)
            {
                httpRequest.HttpContext.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                await httpRequest.HttpContext.Response.WriteAsync("Error while attempting to authorize connection.").ConfigureAwait(false);

                throw ex;
            }
        }

        private async Task WriteUnauthorizedResponseAsync(string headerName, HttpRequest httpRequest)
        {
            httpRequest.HttpContext.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            await httpRequest.HttpContext.Response.WriteAsync($"Unable to authenticate. Missing header: {headerName}").ConfigureAwait(false);
        }
    }
}
