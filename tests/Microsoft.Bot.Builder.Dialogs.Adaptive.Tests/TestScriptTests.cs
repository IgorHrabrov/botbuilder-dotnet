﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
#pragma warning disable SA1201 // Elements should appear in the correct order

using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.Bot.Builder.Dialogs.Adaptive.Tests
{
    [TestClass]
    public class TestScriptTests
    {
        public TestContext TestContext { get; set; }

#if AUTO
        public static IEnumerable<object[]> AssertReplyScripts => TestUtils.GetTestScripts(@"Tests\TestAssertReply");

        [DataTestMethod]
        [DynamicData(nameof(AssertReplyScripts))]
        public async Task TestScript_AssertReply(string resourceId)
        {
            await TestUtils.RunTestScript(resourceId);
        }
#endif

        [TestMethod]
        public async Task TestScriptTests_AssertReplyOneOf()
        {
            await TestUtils.RunTestScript("TestScriptTests_AssertReplyOneOf.test.dialog");
        }

        [TestMethod]
        public async Task TestScriptTests_AssertReplyOneOf_Assertions()
        {
            await TestUtils.RunTestScript("TestScriptTests_AssertReplyOneOf_Assertions.test.dialog");
        }

        [TestMethod]
        public async Task TestScriptTests_AssertReplyOneOf_exact()
        {
            await TestUtils.RunTestScript("TestScriptTests_AssertReplyOneOf_exact.test.dialog");
        }

        [TestMethod]
        public async Task TestScriptTests_AssertReplyOneOf_User()
        {
            await TestUtils.RunTestScript("TestScriptTests_AssertReplyOneOf_User.test.dialog");
        }

        [TestMethod]
        public async Task TestScriptTests_AssertReply_Assertions()
        {
            await TestUtils.RunTestScript("TestScriptTests_AssertReply_Assertions.test.dialog");
        }

        [TestMethod]
        public async Task TestScriptTests_AssertReply_Exact()
        {
            await TestUtils.RunTestScript("TestScriptTests_AssertReply_Exact.test.dialog");
        }

        [TestMethod]
        public async Task TestScriptTests_AssertReply_User()
        {
            await TestUtils.RunTestScript("TestScriptTests_AssertReply_User.test.dialog");
        }

        [TestMethod]
        public async Task TestScriptTests_UserConversationUpdate()
        {
            await TestUtils.RunTestScript("TestScriptTests_UserConversationUpdate.test.dialog");
        }

        [TestMethod]
        public async Task TestScriptTests_UserTyping()
        {
            await TestUtils.RunTestScript("TestScriptTests_UserTyping.test.dialog");
        }
    }
}
