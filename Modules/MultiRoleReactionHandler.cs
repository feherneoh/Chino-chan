﻿using Chino_chan.Models.Settings.Language;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Chino_chan.Modules
{
    public class MultiRoleEntry
    {
        public ulong GuildId { get; set; }
        public ulong ChannelId { get; set; }
        public ulong MessageId { get; set; }
        public Dictionary<string, ulong> EmoteRolePairs { get; set; }
        public bool OnlyOne { get; set; } = false;

        public MultiRoleEntry(ICommandContext Context, ulong MessageId, Dictionary<string, ulong> EmoteRolePairs)
        {
            GuildId = Context.Guild.Id;
            ChannelId = Context.Channel.Id;
            this.MessageId = MessageId;
            this.EmoteRolePairs = EmoteRolePairs;
        }
        public MultiRoleEntry() { }
    }
    public enum CreationState
    {
        WaitMessage = 0,
        WaitEmotes = 1
    }
    public class ActiveMultiRoleCreation
    {
        public ulong UserId { get; set; }
        public ulong ChannelId { get; set; }
        public ulong GuildId { get; set; }

        public CreationState State { get; set; }
        
        public string Message { get; set; }
        public Dictionary<IEmote, ulong> EmoteRolePairs { get; set; }
        public Dictionary<string, ulong> EmoteStringRolePairs
        {
            get
            {
                Dictionary<string, ulong> dict = new Dictionary<string, ulong>();

                foreach (var pair in EmoteRolePairs)
                {
                    dict.Add(pair.Key.ToString(), pair.Value);
                }

                return dict;
            }
        }
        public List<IUserMessage> MessagesToDelete { get; set; }

        public ActiveMultiRoleCreation(ICommandContext Context)
        {
            UserId = Context.User.Id;
            ChannelId = Context.Channel.Id;
            GuildId = Context.Guild.Id;

            State = CreationState.WaitMessage;

            Message = "";
            EmoteRolePairs = new Dictionary<IEmote, ulong>();
            MessagesToDelete = new List<IUserMessage>();
        }

        public bool IsSameChannel(ICommandContext context)
        {
            return context.User.Id == UserId 
                && context.Channel.Id == ChannelId 
                && context.Guild.Id == GuildId;
        }
    }
    public class MultiRoleReactionHandler
    {
        private readonly string Filename = "Data/MultiRoleDatabase.json";
        private readonly Regex RoleRegex;

        public List<MultiRoleEntry> Entries { get; private set; }
        public List<ActiveMultiRoleCreation> ActiveCreations { get; private set; }

        public MultiRoleReactionHandler(DiscordSocketClient Client)
        {
            Entries = new List<MultiRoleEntry>();
            ActiveCreations = new List<ActiveMultiRoleCreation>();
            RoleRegex = new Regex(@"<@&(\d*)>");

            if (File.Exists(Filename))
            {
                Entries.AddRange(JsonConvert.DeserializeObject<MultiRoleEntry[]>(File.ReadAllText(Filename)));
            }

            Client.MessageReceived += discordMessageReceived;
            Client.ReactionAdded += reactionAddAsync;
            Client.ReactionRemoved += reactionRemoveAsync;
            Client.MessageDeleted += messageDeleted;
            Client.ChannelDestroyed += channelDeleted;
            Client.LeftGuild += leftGuild;
        }

        public bool StartInformationFetching(ICommandContext Context)
        {
            if (getCreation(Context) > -1)
                return false;

            ActiveMultiRoleCreation creation = new ActiveMultiRoleCreation(Context);
            creation.MessagesToDelete.Add(Context.Message);
            ActiveCreations.Add(creation);

            return true;
        }

        public bool AddToDelete(ICommandContext Context, IUserMessage Message)
        {
            int index = getCreation(Context);
            if (index > -1)
            {
                ActiveCreations[index].MessagesToDelete.Add(Message);
                return true;
            }
            return false;
        }

        public void Save()
        {
            File.WriteAllText(Filename, JsonConvert.SerializeObject(Entries, Formatting.Indented));
        }

        private Task leftGuild(SocketGuild arg)
        {
            bool save = false;
            for (int i = 0; i < Entries.Count; i++)
            {
                var entry = Entries[i];
                if (entry.GuildId == arg.Id)
                {
                    Entries.RemoveAt(i);
                    i--;
                    save = true;
                }
            }
            if (save)
                Save();
            return Task.CompletedTask;
        }

        private Task channelDeleted(SocketChannel arg)
        {
            List<MultiRoleEntry> entries = new List<MultiRoleEntry>();
            foreach (MultiRoleEntry entry in Entries)
            {
                if (arg is IGuildChannel gCh)
                {
                    if (entry.GuildId == gCh.GuildId && entry.ChannelId == gCh.Id)
                    {
                        entries.Add(entry);
                    }
                }
            }
            if (entries.Count > 0)
            {
                foreach (var entry in entries)
                {
                    Entries.Remove(entry);
                }
                Save();
            }
            return Task.CompletedTask;
        }

        private async Task discordMessageReceived(SocketMessage msg)
        {
            if (!(msg is SocketUserMessage message) || message.Author.IsBot)
                return;

            CommandContext context = new CommandContext(Global.Client, message);

            if (context.Guild == null || !Global.IsAdminOrHigher(message.Author.Id, context.Guild.Id))
                return;

            int index = getCreation(context);

            if (index > -1)
            {
                ActiveMultiRoleCreation creation = ActiveCreations[index];
                if (creation.MessagesToDelete[0].Id == message.Id)
                    return;


                LanguageEntry language = context.GetSettings().GetLanguage();

                creation.MessagesToDelete.Add(message);

                switch (creation.State)
                {
                    case CreationState.WaitMessage:
                        creation.Message = message.Content;
                        creation.MessagesToDelete.Add(await context.Channel.SendMessageAsync(language.GetEntry("MultiRoleSystem:RequestEmotes")));
                        creation.State = CreationState.WaitEmotes;
                        break;
                    case CreationState.WaitEmotes:
                        if (message.Content.Equals("no", StringComparison.InvariantCultureIgnoreCase))
                        {
                            if (creation.EmoteRolePairs.Count == 0)
                            {
                                creation.MessagesToDelete.Add(await context.Channel.SendMessageAsync(language.GetEntry("MultiRoleSystem:MinOne")));
                                break;
                            }
                            await clearAsync(index);
                            await createMessageAsync(context, creation);
                            return;
                        }
                        else if (message.Content.Equals("cancel", StringComparison.InvariantCultureIgnoreCase))
                        {
                            await clearAsync(index);
                            return;
                        }
                        else
                        {
                            string text = message.Content;
                            MatchCollection matches = RoleRegex.Matches(text);

                            if (matches.Count != 1)
                            {
                                creation.MessagesToDelete.Add(await context.Channel.SendMessageAsync(language.GetEntry("MultiRoleSystem:InvalidFormat")));
                                break;
                            }
                            Match roleMatch = matches[0];
                            text = text.Remove(roleMatch.Index, roleMatch.Length).Trim(' ');
                            IRole role = null;

                            if (ulong.TryParse(roleMatch.Groups[1].Value, out ulong roleId))
                            {
                                role = context.Guild.GetRole(roleId);
                            }

                            if (role == null)
                            {
                                creation.MessagesToDelete.Add(await context.Channel.SendMessageAsync(language.GetEntry("MultiRoleSystem:CouldntGetRole")));
                                break;
                            }

                            IEmote emote = null;

                            try
                            {
                                if (Emote.TryParse(text, out Emote e))
                                {
                                    if (await context.Guild.GetEmoteAsync(e.Id) != null)
                                    {
                                        emote = e;
                                    }
                                }
                                else
                                {
                                    try
                                    {
                                        emote = new Emoji(text);
                                    }
                                    catch { }
                                }
                            }
                            catch { }

                            if (emote == null)
                            {
                                creation.MessagesToDelete.Add(await context.Channel.SendMessageAsync(language.GetEntry("MultiRoleSystem:CouldntCreateEmote")));
                            }
                            else
                            {
                                creation.EmoteRolePairs.Add(emote, role.Id);
                                creation.MessagesToDelete.Add(await context.Channel.SendMessageAsync(language.GetEntry("MultiRoleSystem:EmoteAdded")));
                            }
                        }
                        break;
                }

                ActiveCreations[index] = creation;
            }
        }

        private Task messageDeleted(Cacheable<IMessage, ulong> arg1, ISocketMessageChannel arg2)
        {
            if (arg2 is IGuildChannel gCh)
            {
                MultiRoleEntry entry = getEntry(gCh, arg1.Id);
                if (entry != null)
                {
                    Entries.Remove(entry);
                    Save();
                }
            }
            return Task.CompletedTask;
        }

        private async Task createMessageAsync(CommandContext context, ActiveMultiRoleCreation creation)
        {
            IUserMessage message = await context.Channel.SendMessageAsync(creation.Message);
            Entries.Add(new MultiRoleEntry(context, message.Id, creation.EmoteStringRolePairs));
            Save();
            foreach (KeyValuePair<IEmote, ulong> pair in creation.EmoteRolePairs)
            {
                await message.AddReactionAsync(pair.Key);
            }
        }

        private async Task reactionAddAsync(Cacheable<IUserMessage, ulong> cacheable, ISocketMessageChannel channel,
            SocketReaction reaction)
        {
            if (channel is IGuildChannel guildChannel)
            {
                MultiRoleEntry entry = getEntry(guildChannel, reaction.MessageId);
                if (entry != null)
                {
                    string not_animated = reaction.Emote.ToString().Replace("<a:", "<:");
                    string animated = reaction.Emote.ToString().Replace("<:", "<a:");
                    string success = "";

                    if (entry.EmoteRolePairs.ContainsKey(animated))
                    {
                        success = animated;
                    }
                    else if (entry.EmoteRolePairs.ContainsKey(not_animated))
                    {
                        success = not_animated;
                    }

                    if (success != "")
                    {
                        IGuildUser user = reaction.User.GetValueOrDefault() as IGuildUser;
                        if (user.IsBot || user.IsWebhook)
                            return;
                        ulong roleId = entry.EmoteRolePairs[success];
                        if (entry.OnlyOne)
                        {
                            foreach (ulong id in user.RoleIds)
                            {
                                foreach (KeyValuePair<string, ulong> pair in entry.EmoteRolePairs)
                                {
                                    if (pair.Value == id)
                                    {
                                        try
                                        {
                                            await user.RemoveRoleAsync(guildChannel.Guild.GetRole(id));
                                        }
                                        catch { } // can't remove role
                                    }
                                }


                            }
                        }

                        if (!user.RoleIds.Contains(roleId))
                        {
                            if (guildChannel.Guild.GetRole(roleId) is IRole role)
                            {
                                await user.AddRoleAsync(role);
                            }
                        }
                    }
                }
            }
        }
        private async Task reactionRemoveAsync(Cacheable<IUserMessage, ulong> cacheable, ISocketMessageChannel channel,
            SocketReaction reaction)
        {
            if (channel is IGuildChannel guildChannel)
            {
                MultiRoleEntry entry = getEntry(guildChannel, reaction.MessageId);

                if (entry != null)
                {
                    string not_animated = reaction.Emote.ToString().Replace("<a:", "<:");
                    string animated = reaction.Emote.ToString().Replace("<:", "<a:");
                    string success = "";

                    if (entry.EmoteRolePairs.ContainsKey(animated))
                    {
                        success = animated;
                    }
                    else if (entry.EmoteRolePairs.ContainsKey(not_animated))
                    {
                        success = not_animated;
                    }

                    if (success != "")
                    {
                        IGuildUser user = reaction.User.GetValueOrDefault() as IGuildUser;
                        if (user.IsBot || user.IsWebhook)
                            return;
                        ulong roleId = entry.EmoteRolePairs[success];
                        if (user.RoleIds.Contains(roleId))
                        {
                            if (guildChannel.Guild.GetRole(roleId) is IRole role)
                            {
                                await user.RemoveRoleAsync(role);
                            }
                        }
                    }
                }
            }
        }

        private int getCreation(ICommandContext context)
        {
            if (context.Guild == null)
                return -1;
            for (int i = 0; i < ActiveCreations.Count; i++)
            {
                if (ActiveCreations[i].IsSameChannel(context))
                    return i;
            }
            return -1;
        }

        private MultiRoleEntry getEntry(IGuildChannel channel, ulong msgId)
        {
            foreach (MultiRoleEntry entry in Entries)
            {
                if (entry.ChannelId == channel.Id && entry.GuildId == channel.GuildId && entry.MessageId == msgId)
                {
                    return entry;
                }
            }
            return null;
        }

        private async Task clearAsync(int index)
        {
            ActiveMultiRoleCreation creation = ActiveCreations[index];
            foreach (IUserMessage message in creation.MessagesToDelete)
            {
                try
                {
                    await message.DeleteAsync();
                }
                catch { } // has no right to delete, or the message's already deleted
            }
            ActiveCreations.RemoveAt(index);
        }

        
    }
}
