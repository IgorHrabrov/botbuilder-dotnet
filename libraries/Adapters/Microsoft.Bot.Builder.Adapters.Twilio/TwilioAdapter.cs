﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Bot.Builder.Adapters.Twilio
{
    /// <summary>
    /// A <see cref="BotAdapter"/> that can connect to Twilio's SMS service.
    /// </summary>
    public class TwilioAdapter : BotAdapter, IBotFrameworkHttpAdapter
    {
        private readonly TwilioClientWrapper _twilioClient;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="TwilioAdapter"/> class using configuration settings.
        /// </summary>
        /// <param name="configuration">An <see cref="IConfiguration"/> instance.</param>
        /// <remarks>
        /// The configuration keys are:
        /// TwilioNumber: The phone number associated with the Twilio account.
        /// TwilioAccountSid: The string identifier of the account. See https://www.twilio.com/docs/glossary/what-is-a-sid
        /// TwilioAuthToken: The authentication token for the account.
        /// TwilioValidationUrl: The validation URL for incoming requests.
        /// </remarks>
        /// <param name="logger">The ILogger implementation this adapter should use.</param>
        public TwilioAdapter(IConfiguration configuration, ILogger logger = null)
            : this(new TwilioClientWrapper(new TwilioAdapterOptions(configuration["TwilioNumber"], configuration["TwilioAccountSid"], configuration["TwilioAuthToken"], new Uri(configuration["TwilioValidationUrl"]))), logger)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TwilioAdapter"/> class.
        /// </summary>
        /// <param name="twilioClient">The Twilio client to connect to.</param>
        /// <param name="logger">The ILogger implementation this adapter should use.</param>
        public TwilioAdapter(TwilioClientWrapper twilioClient, ILogger logger = null)
        {
            _twilioClient = twilioClient ?? throw new ArgumentNullException(nameof(twilioClient));
            _logger = logger ?? NullLogger.Instance;
        }

        /// <summary>
        /// Sends activities to the conversation.
        /// </summary>
        /// <param name="turnContext">The context object for the turn.</param>
        /// <param name="activities">The activities to send.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by other objects
        /// or threads to receive notice of cancellation.</param>
        /// <returns>A task that represents the work queued to execute.</returns>
        /// <remarks>If the activities are successfully sent, the task result contains
        /// an array of <see cref="ResourceResponse"/> objects containing the SIDs that
        /// Twilio assigned to the activities.</remarks>
        /// <seealso cref="ITurnContext.OnSendActivities(SendActivitiesHandler)"/>
        public override async Task<ResourceResponse[]> SendActivitiesAsync(ITurnContext turnContext, Activity[] activities, CancellationToken cancellationToken)
        {
            var responses = new List<ResourceResponse>();
            foreach (var activity in activities)
            {
                if (activity.Type != ActivityTypes.Message)
                {
                    _logger.LogTrace($"Unsupported Activity Type: '{activity.Type}'. Only Activities of type 'Message' are supported.");
                }
                else
                {
                    var messageOptions = TwilioHelper.ActivityToTwilio(activity, _twilioClient.Options.TwilioNumber);

                    var res = await _twilioClient.SendMessage(messageOptions).ConfigureAwait(false);

                    var response = new ResourceResponse() { Id = res, };

                    responses.Add(response);
                }
            }

            return responses.ToArray();
        }

        /// <summary>
        /// Creates a turn context and runs the middleware pipeline for an incoming activity.
        /// </summary>
        /// <param name="httpRequest">The incoming HTTP request.</param>
        /// <param name="httpResponse">When this method completes, the HTTP response to send.</param>
        /// <param name="bot">The bot that will handle the incoming activity.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by other objects
        /// or threads to receive notice of cancellation.</param>
        /// <returns>A task that represents the work queued to execute.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="httpRequest"/>,
        /// <paramref name="httpResponse"/>, or <paramref name="bot"/> is <c>null</c>.</exception>
        public async Task ProcessAsync(HttpRequest httpRequest, HttpResponse httpResponse, IBot bot, CancellationToken cancellationToken)
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

            Dictionary<string, string> bodyDictionary;
            using (var bodyStream = new StreamReader(httpRequest.Body))
            {
                bodyDictionary = TwilioHelper.QueryStringToDictionary(await bodyStream.ReadToEndAsync().ConfigureAwait(false));
            }

            if (!_twilioClient.ValidateSignature(httpRequest, bodyDictionary))
            {
                throw new Exception("WARNING: Webhook received message with invalid signature. Potential malicious behavior!");
            }

            var activity = TwilioHelper.PayloadToActivity(bodyDictionary);

            // create a conversation reference
            using (var context = new TurnContext(this, activity))
            {
                context.TurnState.Add("httpStatus", HttpStatusCode.OK.ToString("D"));
                await RunPipelineAsync(context, bot.OnTurnAsync, cancellationToken).ConfigureAwait(false);

                var statusCode = Convert.ToInt32(context.TurnState.Get<string>("httpStatus"), CultureInfo.InvariantCulture);
                var text = context.TurnState.Get<object>("httpBody") != null ? context.TurnState.Get<object>("httpBody").ToString() : string.Empty;

                await TwilioHelper.WriteAsync(httpResponse, statusCode, text, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Replaces an existing activity in the conversation.
        /// Twilio SMS does not support this operation.
        /// </summary>
        /// <param name="turnContext">The context object for the turn.</param>
        /// <param name="activity">New replacement activity.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by other objects
        /// or threads to receive notice of cancellation.</param>
        /// <returns>A task that represents the work queued to execute.</returns>
        /// <remarks>This method always returns a faulted task.</remarks>
        /// <seealso cref="ITurnContext.OnUpdateActivity(UpdateActivityHandler)"/>
        public override Task<ResourceResponse> UpdateActivityAsync(ITurnContext turnContext, Activity activity, CancellationToken cancellationToken)
        {
            return Task.FromException<ResourceResponse>(new NotSupportedException("Twilio SMS does not support updating activities."));
        }

        /// <summary>
        /// Deletes an existing activity in the conversation.
        /// Twilio SMS does not support this operation.
        /// </summary>
        /// <param name="turnContext">The context object for the turn.</param>
        /// <param name="reference">Conversation reference for the activity to delete.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by other objects
        /// or threads to receive notice of cancellation.</param>
        /// <returns>A task that represents the work queued to execute.</returns>
        /// <remarks>This method always returns a faulted task.</remarks>
        /// <seealso cref="ITurnContext.OnDeleteActivity(DeleteActivityHandler)"/>
        public override Task DeleteActivityAsync(ITurnContext turnContext, ConversationReference reference, CancellationToken cancellationToken)
        {
            return Task.FromException<ResourceResponse>(new NotSupportedException("Twilio SMS does not support deleting activities."));
        }

        /// <summary>
        /// Sends a proactive message to a conversation.
        /// </summary>
        /// <param name="reference">A reference to the conversation to continue.</param>
        /// <param name="logic">The method to call for the resulting bot turn.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by other objects
        /// or threads to receive notice of cancellation.</param>
        /// <returns>A task that represents the work queued to execute.</returns>
        /// <remarks>Call this method to proactively send a message to a conversation.
        /// Most channels require a user to initiate a conversation with a bot
        /// before the bot can send activities to the user.</remarks>
        /// <seealso cref="BotAdapter.RunPipelineAsync(ITurnContext, BotCallbackHandler, CancellationToken)"/>
        /// <exception cref="ArgumentNullException"><paramref name="reference"/> or
        /// <paramref name="logic"/> is <c>null</c>.</exception>
        public async Task ContinueConversationAsync(ConversationReference reference, BotCallbackHandler logic, CancellationToken cancellationToken)
        {
            if (reference == null)
            {
                throw new ArgumentNullException(nameof(reference));
            }

            if (logic == null)
            {
                throw new ArgumentNullException(nameof(logic));
            }

            var request = reference.GetContinuationActivity().ApplyConversationReference(reference, true);

            using (var context = new TurnContext(this, request))
            {
                await RunPipelineAsync(context, logic, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
