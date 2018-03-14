﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Bot.Schema;

namespace Microsoft.Bot.Builder
{
    public class BotContext : IBotContext
    {
        private readonly BotAdapter _adapter;
        private readonly Activity _request;
        private bool _responded = false;

        private readonly IList<SendActivitiesHandler> _onSendActivities = new List<SendActivitiesHandler>();
        private readonly IList<UpdateActivityHandler> _onUpdateActivity = new List<UpdateActivityHandler>();
        private readonly IList<DeleteActivityHandler> _onDeleteActivity = new List<DeleteActivityHandler>();

        private Dictionary<string, object> _services = new Dictionary<string, object>();

        public BotContext(BotAdapter adapter, Activity request)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _request = request ?? throw new ArgumentNullException(nameof(request));
        }

        public IBotContext OnSendActivity(SendActivitiesHandler handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            _onSendActivities.Add(handler);
            return this;
        }

        public IBotContext OnUpdateActivity(UpdateActivityHandler handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            _onUpdateActivity.Add(handler);
            return this;
        }

        public IBotContext OnDeleteActivity(DeleteActivityHandler handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            _onDeleteActivity.Add(handler);
            return this;
        }

        public BotAdapter Adapter => _adapter;

        public Activity Request => _request;

        /// <summary>
        /// If true at least one response has been sent for the current turn of conversation.
        /// </summary>
        public bool Responded
        {
            get { return _responded; }
            set
            {
                if (value == false)
                {
                    throw new ArgumentException("TurnContext: cannot set 'responded' to a value of 'false'.");
                }
                _responded = true;
            }
        }

        public async Task SendActivity(params string[] textRepliesToSend)
        {
            if (textRepliesToSend == null)
                throw new ArgumentNullException(nameof(textRepliesToSend)); 
            
            List<Activity> newActivities = new List<Activity>();
            foreach (string s in textRepliesToSend)
            {
                if (!string.IsNullOrWhiteSpace(s))
                    newActivities.Add(new Activity(ActivityTypes.Message) { Text = s });
            }

            await SendActivity(newActivities.ToArray());
        }

        public async Task SendActivity(params IActivity[] activities)
        {
            // Bind the relevant Conversation Reference properties, such as URLs and 
            // ChannelId's, to the activities we're about to send. 
            ConversationReference cr = GetConversationReference(this._request);
            foreach (Activity a in activities)
            {                
                ApplyConversationReference(a, cr);
            }

            // Convert the IActivities to Activies. 
            Activity[] activityArray = Array.ConvertAll(activities, (input) => (Activity)input);            

            // Create the list used by the recursive methods. 
            List<Activity> activityList = new List<Activity>(activityArray);

            async Task ActuallySendStuff()
            {
                bool anythingToSend = false;
                if (activities.Count() > 0)
                    anythingToSend = true;

                // Send from the list, which may have been manipulated via the event handlers. 
                await this.Adapter.SendActivity(this, activityList.ToArray());

                // If we actually sent something, set the flag. 
                if (anythingToSend)
                    this.Responded = true;
            }

            await SendActivitiesInternal(activityList, _onSendActivities, ActuallySendStuff);
        }

        /// <summary>
        /// Replaces an existing activity. 
        /// </summary>
        /// <param name="activity">New replacement activity. The activity should already have it's ID information populated</param>        
        public async Task UpdateActivity(IActivity activity)
        {
            Activity a = (Activity)activity; 

            async Task ActuallyUpdateStuff()
            {
                await this.Adapter.UpdateActivity(this, a);
            }

            await UpdateActivityInternal(a, _onUpdateActivity, ActuallyUpdateStuff);
        }

        public async Task DeleteActivity(string activityId)
        {
            if (string.IsNullOrWhiteSpace(activityId))
                throw new ArgumentNullException(nameof(activityId));

            ConversationReference cr = GetConversationReference(this.Request);
            cr.ActivityId = activityId;

            async Task ActuallyDeleteStuff()
            {
                await this.Adapter.DeleteActivity(this, cr);
            }

            await DeleteActivityInternal(cr, _onDeleteActivity, ActuallyDeleteStuff);
        }

        private async Task SendActivitiesInternal(
            List<Activity> activities,
            IEnumerable<SendActivitiesHandler> sendHandlers,
            Func<Task> callAtBottom)
        {
            if (activities == null)
                throw new ArgumentException(nameof(activities));
            if (sendHandlers == null)
                throw new ArgumentException(nameof(sendHandlers));

            if (sendHandlers.Count() == 0) // No middleware to run.
            {
                if (callAtBottom != null)
                    await callAtBottom();

                return;
            }

            // Default to "No more Middleware after this"
            async Task next()
            {
                // Remove the first item from the list of middleware to call,
                // so that the next call just has the remaining items to worry about. 
                IEnumerable<SendActivitiesHandler> remaining = sendHandlers.Skip(1);
                await SendActivitiesInternal(activities, remaining, callAtBottom).ConfigureAwait(false);
            }

            // Grab the current middleware, which is the 1st element in the array, and execute it            
            SendActivitiesHandler caller = sendHandlers.First();
            await caller(this, activities, next);
        }

        private async Task UpdateActivityInternal(Activity activity,
            IEnumerable<UpdateActivityHandler> updateHandlers,
            Func<Task> callAtBottom)
        {
            BotAssert.ActivityNotNull(activity);
            if (updateHandlers == null)
                throw new ArgumentException(nameof(updateHandlers));

            if (updateHandlers.Count() == 0) // No middleware to run.
            {
                if (callAtBottom != null)
                {
                    await callAtBottom();
                }

                return;
            }

            // Default to "No more Middleware after this"
            async Task next()
            {
                // Remove the first item from the list of middleware to call,
                // so that the next call just has the remaining items to worry about. 
                IEnumerable<UpdateActivityHandler> remaining = updateHandlers.Skip(1);
                await UpdateActivityInternal(activity, remaining, callAtBottom).ConfigureAwait(false);
            }

            // Grab the current middleware, which is the 1st element in the array, and execute it            
            UpdateActivityHandler toCall = updateHandlers.First();
            await toCall(this, activity, next);
        }

        private async Task DeleteActivityInternal(ConversationReference cr,
           IEnumerable<DeleteActivityHandler> updateHandlers,
           Func<Task> callAtBottom)
        {
            BotAssert.ConversationReferenceNotNull(cr);
            if (updateHandlers == null)
                throw new ArgumentException(nameof(updateHandlers));

            if (updateHandlers.Count() == 0) // No middleware to run.
            {
                if (callAtBottom != null)
                {
                    await callAtBottom();
                }

                return;
            }

            // Default to "No more Middleware after this"
            async Task next()
            {
                // Remove the first item from the list of middleware to call,
                // so that the next call just has the remaining items to worry about. 
                IEnumerable<DeleteActivityHandler> remaining = updateHandlers.Skip(1);
                await DeleteActivityInternal(cr, remaining, callAtBottom).ConfigureAwait(false);
            }

            // Grab the current middleware, which is the 1st element in the array, and execute it            
            DeleteActivityHandler toCall = updateHandlers.First();
            await toCall(this, cr, next);
        }
       
        /// <summary>
        /// Set the value associated with a key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value to set.</param>
        public void Set(string key, object value)
        {
            if (String.IsNullOrWhiteSpace(key))
                throw new ArgumentNullException(nameof(key));

            lock (_services)
            {
                _services[key] = value;
            }
        }

        /// <summary>
        /// Get a value by a key.
        /// </summary>
        /// <param name="key">The key of the value to get.</param>
        /// <returns>The value.</returns>
        public object Get(string key)
        {
            if (String.IsNullOrWhiteSpace(key))
                throw new ArgumentNullException(nameof(key));

            object service = null;
            lock (_services)
            {
                _services.TryGetValue(key, out service);
            }
            return service;
        }

        /// <summary>
        /// Determins if a key been set in the Cache
        /// </summary>
        /// <param name="key">The key of the value to get.</param>
        /// <returns>True, if the key is found. False, if not.</returns>
        public bool Has(string key)
        {
            if (String.IsNullOrWhiteSpace(key))
                throw new ArgumentNullException(nameof(key));

            lock (_services)
            {
                return _services.ContainsKey(key);
            }
        }

        /// <summary>
        /// Creates a Conversation Reference from an Activity
        /// </summary>
        /// <param name="activity">The activity to update. Existing values in the Activity will be overwritten.</param>        
        public static ConversationReference GetConversationReference(Activity activity)
        {
            BotAssert.ActivityNotNull(activity);

            ConversationReference r = new ConversationReference
            {
                ActivityId = activity.Id,
                User = activity.From,
                Bot = activity.Recipient,
                Conversation = activity.Conversation,
                ChannelId = activity.ChannelId,
                ServiceUrl = activity.ServiceUrl
            };

            return r;
        }

        /// <summary>
        /// Updates an activity with the delivery information from a conversation reference. Calling
        /// this after[getConversationReference()] (#getconversationreference) on an incoming activity 
        /// will properly address the reply to a received activity.
        /// </summary>
        /// <param name="a">Activity to copy delivery information to</param>
        /// <param name="r">Conversation reference containing delivery information</param>
        /// <param name="isIncoming">(Optional) flag indicating whether the activity is an incoming or outgoing activity. Defaults to `false` indicating the activity is outgoing.</param>
        public static Activity ApplyConversationReference(Activity a, ConversationReference r, bool isIncoming = false)
        {
            a.ChannelId = r.ChannelId;
            a.ServiceUrl = r.ServiceUrl;
            a.Conversation = r.Conversation;

            if (isIncoming)
            {
                a.From = r.User;
                a.Recipient = r.Bot;
                if (r.ActivityId != null)
                    a.Id = r.ActivityId;
            }
            else  // Outoing
            {
                a.From = r.Bot;
                a.Recipient = r.User;
                if (r.ActivityId != null)
                    a.ReplyToId = r.ActivityId;
            }
            return a;
        }
    }
}
