﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
#pragma warning disable SA1402 // File may only contain a single type
#pragma warning disable SA1515 // Single-line comment should be preceded by blank line

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Actions;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Conditions;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Input;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Templates;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Microsoft.Bot.Builder.Dialogs.Adaptive.Tests
{
    [TestClass]
    public class MiscTests
    {
        public TestContext TestContext { get; set; }

        [TestMethod]
        public async Task IfCondition_EndDialog()
        {
            await TestUtils.RunTestScript("IfCondition_EndDialog.test.dialog");
        }

        [TestMethod]
        public async Task Rule_Reprompt()
        {
            await TestUtils.RunTestScript("Rule_Reprompt.test.dialog");
        }

        [TestMethod]
        public async Task DialogManager_InitDialogsEnsureDependencies()
        {
            Dialog CreateDialog()
            {
                return new AdaptiveDialog()
                {
                    Recognizer = new CustomRecognizer(),
                    Triggers = new List<Adaptive.Conditions.OnCondition>()
                    {
                        new OnUnknownIntent()
                        {
                            Actions = new List<Dialog>()
                            {
                                new SendActivity("You said '@{turn.activity.text}'"),
                                new TextInput()
                                {
                                    Prompt = new ActivityTemplate("Enter age"),
                                    Property = "$age"
                                },
                                new SendActivity("You said @{$age}")
                            }
                        }
                    }
                };
            }

            await new TestScript()
                {
                    Dialog = CreateDialog()
                }
                .Send("hi")
                    .AssertReply("You said 'hi'")
                    .AssertReply("Enter age")
                .Send("10")
                    .AssertReply("You said 10")
                .ExecuteAsync();

            // create new dialog manager and new dialog each turn should be the same as when it's static
            await new TestScript()
                .Send("hi")
                    .AssertReply("You said 'hi'")
                    .AssertReply("Enter age")
                .Send("10")
                    .AssertReply("You said 10")
                .ExecuteAsync(callback: (context, ct) => new DialogManager(CreateDialog()).OnTurnAsync(context, ct));
        }
    }

    public class CustomRecognizer : IRecognizer
    {
        public Task<RecognizerResult> RecognizeAsync(ITurnContext turnContext, CancellationToken cancellationToken)
        {
            return Task.FromResult(new RecognizerResult());
        }

        public Task<T> RecognizeAsync<T>(ITurnContext turnContext, CancellationToken cancellationToken)
            where T : IRecognizerConvert, new()
        {
            throw new NotImplementedException();
        }
    }
}
