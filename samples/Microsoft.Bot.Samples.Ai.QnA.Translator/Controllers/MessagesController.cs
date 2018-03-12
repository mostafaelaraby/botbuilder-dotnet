﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Ai;
using Microsoft.Bot.Builder.BotFramework;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.Bot.Builder.Storage;

namespace Microsoft.Bot.Samples.Ai.QnA.Controllers
{
    [Route("api/[controller]")]
    public class MessagesController : Controller
    {

        private static readonly HttpClient _httpClient = new HttpClient();
        BotFrameworkAdapter _adapter;

        //supported langauges and locales
        private static readonly string[] _supportedLanguages = new string[] { "en", "fr" };
        private static readonly string[] _supportedLocales = new string[] { "fr-fr", "en-us" };
        private static string currentLanguage = null;
        private static string currentLocale = null;


        public MessagesController(IConfiguration configuration)
        {
            var qnaMiddlewareOptions = new QnAMakerMiddlewareOptions
            {
                // add subscription key and knowledge base id
                SubscriptionKey = "ff9de8cc5fc94e759ea6441fa7311a2f",
                KnowledgeBaseId = "b7137e1b-5a5b-43f2-870f-56aa2965dff1"
            };


            var bot = new Builder.Bot(new BotFrameworkAdapter(configuration))
                .Use(new BotStateManager(new FileStorage(System.IO.Path.GetTempPath()))) //store user state in a temp directory
                .Use(new TranslationMiddleware(new string[] { "en" }, "e1046bee4d8d4657984a584c876302e5", "", GetActiveLanguage, SetActiveLanguage))
                .Use(new LocaleConverterMiddleware(GetActiveLocale, SetActiveLocale, "en-us", new LocaleConverter()))
                //LocaleConverter and Translation middleware use default values for source language and from locale
                // add QnA middleware 
                .Use(new QnAMakerMiddleware(qnaMiddlewareOptions, _httpClient));

            bot.OnReceive(BotReceiveHandler);

            _adapter = (BotFrameworkAdapter)bot.Adapter;
        }

        private Task BotReceiveHandler(IBotContext context)
        {
            if (context.Request.Type == ActivityTypes.Message && context.Responses.Count == 0)
            {
                // add app logic when QnA Maker doesn't find an answer
                context.Reply("No good match found in the KB.");
            }
            //context.Reply(context.Request.AsMessageActivity().Text);
            return Task.CompletedTask;
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromBody]Activity activity)
        {
            try
            {
                await _adapter.Receive(this.Request.Headers["Authorization"].FirstOrDefault(), activity);
                return this.Ok();
            }
            catch (UnauthorizedAccessException)
            {
                return this.Unauthorized();
            }
        }

        //Change language and locale
        [HttpGet]
        public IActionResult Get(string lang, string locale)
        {
            currentLanguage = lang;
            currentLocale = locale;
            return new ObjectResult("Success!");
        }

        private void SetLanguage(IBotContext context, string language) => context.State.User[@"Microsoft.API.translateTo"] = language;
        private void SetLocale(IBotContext context, string locale) => context.State.User[@"LocaleConverterMiddleware.fromLocale"] = locale;

        protected bool IsSupportedLanguage(string language) => _supportedLanguages.Contains(language);
        protected async Task<bool> SetActiveLanguage(IBotContext context)
        {
            bool changeLang = false;//logic implemented by developper to make a signal for language changing 
            //use a specific message from user to change language
            var messageActivity = context.Request.AsMessageActivity();
            if (messageActivity.Text.ToLower().StartsWith("set my language to"))
            {
                changeLang = true;
            }
            if (changeLang)
            {
                var newLang = messageActivity.Text.ToLower().Replace("set my language to", "").Trim();
                if (!string.IsNullOrWhiteSpace(newLang)
                        && IsSupportedLanguage(newLang))
                {
                    SetLanguage(context, newLang);
                    context.Reply($@"Changing your language to {newLang}");
                }
                else
                {
                    context.Reply($@"{newLang} is not a supported language.");
                }
                //intercepts message
                return true;
            }

            return false;
        }
        protected string GetActiveLanguage(IBotContext context)
        {
            if (currentLanguage != null)
            {
                //user has specified a different language so update the bot state
                if (currentLanguage != (string)context.State.User[@"Microsoft.API.translateTo"])
                {
                    SetLanguage(context, currentLanguage);
                }
            }
            if (context.Request.Type == ActivityTypes.Message
                && context.State.User.ContainsKey(@"Microsoft.API.translateTo"))
            {
                return (string)context.State.User[@"Microsoft.API.translateTo"];
            }

            return "en";
        }
        protected async Task<bool> SetActiveLocale(IBotContext context)
        {
            bool changeLocale = false;//logic implemented by developper to make a signal for language changing 
            //use a specific message from user to change language
            var messageActivity = context.Request.AsMessageActivity();
            if (messageActivity.Text.ToLower().StartsWith("set my locale to"))
            {
                changeLocale = true;
            }
            if (changeLocale)
            {
                var newLocale = messageActivity.Text.ToLower().Replace("set my locale to", "").Trim(); //extracted by the user using user state 
                if (!string.IsNullOrWhiteSpace(newLocale)
                        && IsSupportedLanguage(newLocale))
                {
                    SetLocale(context, newLocale);
                    context.Reply($@"Changing your language to {newLocale}");
                }
                else
                {
                    context.Reply($@"{newLocale} is not a supported locale.");
                }
                //intercepts message
                return true;
            }

            return false;
        }
        protected string GetActiveLocale(IBotContext context)
        {
            if (currentLocale != null)
            {
                //the user has specified a different locale so update the bot state
                if (currentLocale != (string)context.State.User[@"LocaleConverterMiddleware.fromLocale"])
                {
                    SetLocale(context, currentLocale);
                }
            }
            if (context.Request.Type == ActivityTypes.Message
                && context.State.User.ContainsKey(@"LocaleConverterMiddleware.fromLocale"))
            {
                return (string)context.State.User[@"LocaleConverterMiddleware.fromLocale"];
            }

            return "en-us";
        }
    }
}
