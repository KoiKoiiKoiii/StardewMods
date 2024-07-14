﻿using System.Text;
using DynamicDialogues.Commands;
using DynamicDialogues.Framework;
using DynamicDialogues.Models;
using DynamicDialogues.Patches;
using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Triggers;
using static DynamicDialogues.Framework.Parser;
using static DynamicDialogues.Framework.Getter;

// ReSharper disable All

namespace DynamicDialogues;

public class ModEntry : Mod
{
    public override void Entry(IModHelper helper)
    {
        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.Events.GameLoop.SaveLoaded += SaveLoaded;

        helper.Events.GameLoop.ReturnedToTitle += OnTitleReturn;

        //set file type, npc dialogues, etc
        helper.Events.GameLoop.TimeChanged += Setter.OnTimeChange;
        helper.Events.GameLoop.DayEnding += Setter.OnDayEnd;
        helper.Events.Content.AssetRequested += Setter.OnAssetRequest;
        helper.Events.Content.AssetsInvalidated += Setter.ReloadAssets;

        Config = this.Helper.ReadConfig<ModConfig>();
        Mon = this.Monitor;
        Help = this.Helper;

#if DEBUG
        helper.ConsoleCommands.Add("ddprint", "Prints dialogue type", Debug.Print);
        helper.ConsoleCommands.Add("sayHiTo", "Test sayHiTo command", Debug.SayHiTo);
        helper.ConsoleCommands.Add("getQs", "Get NPC questions", Debug.GetQuestionsFor);
#endif
        
        var harmony = new Harmony(this.ModManifest.UniqueID);

        DialoguePatches.Apply(harmony);
        EventPatches.Apply(harmony);
        NPCPatches.Apply(harmony);

        // TAS related
        Event.RegisterCommand("AddScene", AnimatedSprites.AddScene);
        Event.RegisterCommand("RemoveScene", AnimatedSprites.RemoveScene);
        Event.RegisterCommand("addFire", AnimatedSprites.AddFire);

        // world related
        Event.RegisterCommand("objectHunt", World.ObjectHunt);

        // event extension
        Event.RegisterCommand("if", Extensions.IfElse);
        Event.RegisterCommand("append", Extensions.Append);

        // character related
        Event.RegisterCommand("resetName", Characters.ResetName);
        Event.RegisterCommand("changeDating", Characters.SetDating);
        
        // player related
        Event.RegisterCommand("health", Player.Health);
        Event.RegisterCommand("stamina", Player.Stamina);
        Event.RegisterCommand("multiplayerMail", Player.MultiplayerMail);
        Event.RegisterCommand("addExp", Player.AddExp);
        
        // trigger actions
        TriggerActionManager.RegisterAction("mistyspring.dynamicdialogues_DoEvent", TriggerActions.DoEvent);
        TriggerActionManager.RegisterAction("mistyspring.dynamicdialogues_SendNotification", TriggerActions.SendNotification);
        TriggerActionManager.RegisterAction("mistyspring.dynamicdialogues_Speak", TriggerActions.Speak);
        TriggerActionManager.RegisterAction("mistyspring.dynamicdialogues_Exp", TriggerActions.AddExp);

        // game state queries
        GameStateQuery.Register("mistyspring.dynamicdialogues_EventLocked", Queries.EventLocked);
        GameStateQuery.Register("mistyspring.dynamicdialogues_PlayerWearing", Queries.Wearing);

    }

    public override object GetApi() =>new Api();
    
    private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
    {
        var configMenu = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (configMenu is null) return;
        // register mod
        configMenu.Register(
            mod: ModManifest,
            reset: () => Config = new ModConfig(),
            save: () => Helper.WriteConfig(Config)
        );

        configMenu.AddNumberOption(
            mod: ModManifest,
            name: () => Helper.Translation.Get("config.QuestChance.name"),
            tooltip: () => Helper.Translation.Get("config.QuestChance.description"),
            getValue: () => Config.QuestChance,
            setValue: value => Config.QuestChance = value,
            min: 0,
            max: 100
        );
        
        configMenu.SetTitleScreenOnlyForNextOptions(ModManifest, true);
        configMenu.AddBoolOption(
            mod: ModManifest,
            name: () =>
            {
                var t = Helper.Translation.Get("config.change_@.name");
                var s = new StringBuilder(t);
                if (s.Length > 20)
                {
                    var secondspace = false;
                    for (var i = 0; i < s.Length; i++)
                    {
                        if (s[i] != ' ')
                            continue;
                        
                        if (!secondspace)
                        {
                            secondspace = true;
                            continue;
                        }
                        
                        s[i] = '\n';
                        break;
                    }
                }
                return s.ToString();
            },
            tooltip: () => Helper.Translation.Get("config.change_@.description"),
            getValue: () => Config.ChangeAt,
            setValue: value => Config.ChangeAt = value
            );
        
        configMenu.SetTitleScreenOnlyForNextOptions(ModManifest, false);

        configMenu.AddSectionTitle(
            mod: ModManifest,
            text: () => Helper.Translation.Get("config.debug.name")
            );
        configMenu.AddBoolOption(
            mod: ModManifest,
            name: () => Helper.Translation.Get("config.debug.name"),
            tooltip: () => Helper.Translation.Get("config.debug.description"),
            getValue: () => Config.Debug,
            setValue: value => Config.Debug = value
            );
        configMenu.AddBoolOption(
            mod: ModManifest,
            name: () => Helper.Translation.Get("config.verbose.name"),
            tooltip: () => Helper.Translation.Get("config.verbose.description"),
            getValue: () => Config.Verbose,
            setValue: value => Config.Verbose = value
            );
    }

    private void SaveLoaded(object sender, SaveLoadedEventArgs e)
    {
        GetInteractableNPCs();

        this.Monitor.Log($"Found {PatchableNPCs?.Count ?? 0} patchable characters.", LogLevel.Debug);
        this.Monitor.Log($"Found {Game1.player.friendshipData?.Length ?? 0} characters in friendship data.", LogLevel.Debug);

        //get dialogue for NPCs
        foreach (var name in PatchableNPCs)
        {
            if (!Exists(name)) //if NPC doesnt exist in savedata
            {
                this.Monitor.Log($"{name} data won't be added. Check log for more details.");
                continue;
            }
            if (Config.Verbose)
            {
                this.Monitor.Log($"Checking patch data for NPC {name}...");
            }
            var CompatRaw = Helper.GameContent.Load<Dictionary<string, DialogueData>>($"mistyspring.dynamicdialogues/Dialogues/{name}");
            GetNPCDialogues(CompatRaw, name);

            //get questions
            var QRaw = Helper.GameContent.Load<Dictionary<string, QuestionData>>($"mistyspring.dynamicdialogues/Questions/{name}");
            GetQuestions(QRaw, name);
        }

        RemoveAnyEmpty();

        var dc = Dialogues?.Count ?? 0;
        this.Monitor.Log($"Loaded {dc} user patches. (Dialogues)", LogLevel.Debug);

        var qc = Questions?.Count ?? 0;
        this.Monitor.Log($"Loaded {qc} user patches. (Questions)", LogLevel.Debug);

        //get greetings
        var greetRaw = Helper.GameContent.Load<Dictionary<string, Dictionary<string, string>>>("mistyspring.dynamicdialogues/Greetings");
        GetGreetings(greetRaw);
        var gc = Greetings?.Count ?? 0;
        this.Monitor.Log($"Loaded {gc} user patches. (Greetings)", LogLevel.Debug);

        //get notifs
        var notifRaw = Helper.GameContent.Load<Dictionary<string, NotificationData>>("mistyspring.dynamicdialogues/Notifs");
        GetNotifs(notifRaw);
        var nc = Notifs?.Count ?? 0;
        this.Monitor.Log($"Loaded {nc} user patches. (Notifs)", LogLevel.Debug);

        //get random dialogue
        GetDialoguePool();
        var rr = RandomPool?.Count ?? 0;
        this.Monitor.Log($"Loaded {rr} user patches. (Dialogue pool)", LogLevel.Debug);

        this.Monitor.Log($"{dc + gc + nc + qc + rr} total user patches loaded.", LogLevel.Debug);
    }

    private void OnTitleReturn(object sender, ReturnedToTitleEventArgs e)
    {
        AlreadyPatched?.Clear();
        Dialogues?.Clear();
        Greetings?.Clear();
        Notifs?.Clear();
        Questions?.Clear();
        QuestionCounter?.Clear();
        RandomPool?.Clear();
        PatchableNPCs?.Clear();
    }

    #region methods to get data
    // do NOT change unless fixes are required

    internal static void GetNPCDialogues(Dictionary<string, DialogueData> raw, string nameof)
    {
        foreach (var singular in raw)
        {
#if DEBUG
            Log($"Checking dialogue with key {singular.Key}...",LogLevel.Debug);
#endif
            var dialogueInfo = singular.Value;
            if (dialogueInfo is null)
            {
                Log($"The dialogue data for {nameof} ({singular.Key}) is empty!", LogLevel.Warn);
            }
            else if (IsValid(dialogueInfo, nameof))
            {
                Log($"Dialogue key \"{singular.Key}\" ({nameof}) parsed successfully. Adding to dictionary");
                var data = dialogueInfo;

                if ((bool)(Dialogues?.ContainsKey(nameof)))
                {
                    Dialogues[nameof].Add(data);
                }
                else
                {
                    var list = new List<DialogueData>();
                    list.Add(data);
                    Dialogues.Add(nameof, list);
                }
            }
            else
            {
                Log($"Patch '{singular.Key}' won't be added.", LogLevel.Warn);
            }
        }
    }

    internal static void GetGreetings(Dictionary<string, Dictionary<string, string>> greetRaw)
    {
        foreach (var edit in greetRaw)
        {
            NPC mainCh = Game1.getCharacterFromName(edit.Key);
            if (!CanBePatched(mainCh))
            {
                continue;
            }

            Log($"Loading greetings for {edit.Key}...");
            Dictionary<NPC, string> ValueOf = new();

            foreach (var npcgreet in edit.Value)
            {
                Log($"Checking greet data for {npcgreet.Key}...");
                var chara = Game1.getCharacterFromName(npcgreet.Key);

                if (IsValidGreeting(chara, npcgreet.Value))
                {
                    Greetings.Add((edit.Key, npcgreet.Key), npcgreet.Value);
                    Log("Greeting added.");
                }
            }
        }
    }

    internal static void GetNotifs(Dictionary<string, NotificationData> notifRaw)
    {
        foreach (var pair in notifRaw)
        {
            var notif = pair.Value;
            if (IsValidNotif(notif))
            {
                Log($"Notification \"{pair.Key}\" parsed successfully.");
                Notifs.Add(notif);
            }
            else
            {
                Log($"Found error in \"{pair.Key}\" while parsing, check Log for details.", LogLevel.Error);
            }
        }
    }

    internal static void GetQuestions(Dictionary<string, QuestionData> QRaw, string nameof)
    {
        Questions.Remove(nameof);

        var dict = new List<QuestionData>();
        Questions.Add(nameof, dict);

        foreach (var extra in QRaw)
        {
            if (Config.Debug)
                Log($"checking question data: {nameof}, answer: {extra.Value.Answer}", LogLevel.Debug);

            var title = extra.Key;
            var QnA = extra.Value;
            if (IsValidQuestion(QnA) && !String.IsNullOrWhiteSpace(title))
            {
                Questions[nameof].Add(QnA);
            }
            else
            {
                var pos = GetIndex(QRaw, extra.Key);
                Log($"Entry {pos} for {extra.Key} is faulty! It won't be added.", LogLevel.Warn);
            }
        }
    }
    private void GetDialoguePool()
    {
        foreach (var name in PatchableNPCs)
        {
            if (Game1.player.friendshipData.ContainsKey(name))
            {
                if (Config.Verbose)
                {
                    this.Monitor.Log($"Character {name} found.");
                }

                var dialogues = Help.GameContent.Load<Dictionary<string, string>>($"Characters/Dialogue/{name}");

                if (dialogues == null || dialogues.Count == 0)
                    continue;

                List<string> texts = new();

                foreach (var pair in dialogues)
                {
                    if (pair.Key.StartsWith("Random"))
                    {
                        texts.Add(pair.Value);
                    }
                }

                //dont add npcs with no dialogue
                if (texts == null || texts.Count == 0)
                {
                    continue;
                }

                RandomPool.Add(name, texts);
            }
            else
            {
                if (name == "Marlon")
                    continue; //we dont warn bc hes not interactable

                this.Monitor.Log($"Character {name} hasn't been met yet. Any of their custom dialogue won't be added.");
            }
        }
    }
    
    internal static void GetInteractableNPCs()
    {
        PatchableNPCs = new();

        foreach (var charData in Game1.characterData)
        {
            //var data = charData.Value;
            var name = charData.Key;

            if(Game1.content.DoesAssetExist<Dictionary<string, string>>("Characters/Dialogue/" + name))
                PatchableNPCs.Add(name);
        }
    }
    
    internal static void RemoveAnyEmpty()
    {
        List<string> toremove = new();

        foreach (var pair in Questions)
        {
            if (!pair.Value.Any())
                toremove.Add(pair.Key);
        }

        foreach (var name in toremove)
        {
            Questions.Remove(name);
        }
    }
    #endregion
    /* Required by mod to work */
    #region own data
    public static Dictionary<string, List<QuestionData>> Questions { get; internal set; } = new();
    public static Dictionary<string, List<DialogueData>> Dialogues { get; internal set; } = new();
    public static Dictionary<(string, string), string> Greetings { get; internal set; } = new();
    public static List<NotificationData> Notifs { get; internal set; } = new();
    public static Dictionary<string, List<string>> RandomPool { get; internal set; } = new();
    public static Dictionary<string, int> QuestionCounter { get; internal set; } = new();
    #endregion

    #region event specific
    internal static List<EventData> EventQueue { get; set; } = new();
    internal static bool EventLock { get; set; }
    #endregion

    #region variable data
    internal static List<string> PatchableNPCs { get; private set; } = new();
    internal static List<(string, string, string)> AlreadyPatched { get; set; } = new();
    #endregion

    internal static IMonitor Mon { get; private set; }
    internal static void Log(string msg, LogLevel lv = Level) => Mon.Log(msg, lv);
#if DEBUG
    internal const LogLevel Level = LogLevel.Debug;
#else
    internal const LogLevel Level =  LogLevel.Trace;
#endif
    internal static IModHelper Help { get; private set; }
    internal static ModConfig Config;
}