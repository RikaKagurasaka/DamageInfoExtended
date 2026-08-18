using Dalamud.Game.Command;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Gui.FlyText;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using DamageInfoPlugin.Positionals;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Dalamud.Bindings.ImGui;
using static DamageInfoPlugin.LogType;
using Action = Lumina.Excel.Sheets.Action;
using Status = Lumina.Excel.Sheets.Status;
using Character = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using DObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace DamageInfoPlugin;

// ReSharper disable once ClassNeverInstantiated.Global
public unsafe class DamageInfoPlugin : IDalamudPlugin
{
	private const int TargetInfoGaugeBgNodeId = 15;
	private const int TargetInfoGaugeNodeId = 13;

	private const int TargetInfoSplitGaugeBgNodeId = 7;
	private const int TargetInfoSplitGaugeNodeId = 5;

	private const int FocusTargetInfoGaugeBgNodeId = 8;
	private const int FocusTargetInfoGaugeNodeId = 6;

	public string Name => "Damage Info Extended";

	private const string CommandName = "/dmginfoext";

	private readonly Configuration _configuration;
	private readonly PluginUI _ui;

	private delegate void AddScreenLogDelegate(
		Character* target,
		Character* source,
		FlyTextKind logKind,
		int option,
		int actionKind,
		int actionId,
		int val1,
		int val2,
		int val3,
		int val4);

	private delegate void SetCastBarDelegate(IntPtr thisPtr, IntPtr a2, IntPtr a3, IntPtr a4, char a5);

	private readonly Hook<AddScreenLogDelegate> _addScreenLogHook;
	private readonly Hook<ActionEffectHandler.Delegates.Receive> _receiveActionEffectHook;
	private readonly Hook<SetCastBarDelegate> _setCastBarHook;
	private readonly Hook<SetCastBarDelegate> _setFocusTargetCastBarHook;

	private readonly CastbarInfo _nullCastbarInfo;
	private Dictionary<uint, DamageType> _actionToDamageTypeDict;
	private Dictionary<uint, string> _actionToNameDict;
	private readonly HashSet<uint> _ignoredCastActions;
	private ActionEffectStore _actionStore;
    private readonly Dictionary<ulong, string>? _petNicknamesDictionary;

    private readonly PositionalManager _posManager;

    private int _positionalsHit;
    private int _positionalsAttempted;
    private DateTime _combatStartTime;

	public DamageInfoPlugin(IDalamudPluginInterface pi)
	{
		DalamudApi.Initialize(pi);
		
		_configuration = LoadConfig();
		_ui = new PluginUI(_configuration, this);
		_actionToDamageTypeDict = new Dictionary<uint, DamageType>();

		try
		{
			_petNicknamesDictionary = DalamudApi.PluginInterface.GetOrCreateData("PetRenamer.GameObjectRenameDict", () => new Dictionary<ulong, string>());
		}
		catch { }
			
		_actionToNameDict = new Dictionary<uint, string>();
		_ignoredCastActions = new HashSet<uint>();
        _actionStore = new ActionEffectStore(_configuration);
		_nullCastbarInfo = new CastbarInfo
		{
			unitBase = null,
			gauge = null,
			bg = null,
		};
		_posManager = new PositionalManager();

		DalamudApi.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
		{
			HelpMessage = "Display the Damage Info Extended configuration interface.",
		});

		try
		{
			var actionSheet = DalamudApi.DataManager.GetExcelSheet<Action>();

			if (actionSheet == null)
				throw new NullReferenceException();

			foreach (var row in actionSheet)
			{
				var dmgType = ((AttackType)row.AttackType.RowId).ToDamageType();
				var name = row.Name;
				
				_actionToDamageTypeDict.Add(row.RowId, dmgType);
				_actionToNameDict.Add(row.RowId, name.ToString());

				if (row.ActionCategory.RowId is > 4 and < 11)
					_ignoredCastActions.Add(row.ActionCategory.RowId);
			}

			_receiveActionEffectHook = DalamudApi.Hooks.HookFromAddress<ActionEffectHandler.Delegates.Receive>(
				ActionEffectHandler.MemberFunctionPointers.Receive,
				ReceiveActionEffect);

			var addScreenLogPtr = DalamudApi.SigScanner.ScanText("E8 ?? ?? ?? ?? BF ?? ?? ?? ?? EB 39");
			_addScreenLogHook = DalamudApi.Hooks.HookFromAddress<AddScreenLogDelegate>(addScreenLogPtr, AddScreenLogDetour);

			var setCastBarFuncPtr = DalamudApi.SigScanner.ScanText("E8 ?? ?? ?? ?? 4C 8D 8F ?? ?? ?? ?? 4D 8B C6 48 8B D5");
			_setCastBarHook = DalamudApi.Hooks.HookFromAddress<SetCastBarDelegate>(setCastBarFuncPtr, SetCastBarDetour);

			var setFocusTargetCastBarFuncPtr = DalamudApi.SigScanner.ScanText("40 53 56 41 54 41 57 48 83 EC 78");
			_setFocusTargetCastBarHook = DalamudApi.Hooks.HookFromAddress<SetCastBarDelegate>(setFocusTargetCastBarFuncPtr, SetFocusTargetCastBarDetour);

			DalamudApi.FlyTextGui.FlyTextCreated += OnFlyTextCreated;
		}
		catch (Exception ex)
		{
			DalamudApi.PluginLog.Error(ex, $"An error occurred loading DamageInfoPlugin.");
			DalamudApi.PluginLog.Error("Plugin will not be loaded.");

			_addScreenLogHook?.Disable();
			_addScreenLogHook?.Dispose();
			_receiveActionEffectHook?.Disable();
			_receiveActionEffectHook?.Dispose();
			_setCastBarHook?.Disable();
			_setCastBarHook?.Dispose();
			_setFocusTargetCastBarHook?.Disable();
			_setFocusTargetCastBarHook?.Dispose();
			DalamudApi.CommandManager.RemoveHandler(CommandName);

			throw;
		}

		_receiveActionEffectHook.Enable();
		_addScreenLogHook.Enable();
		_setCastBarHook.Enable();
		_setFocusTargetCastBarHook.Enable();

		DalamudApi.PluginInterface.UiBuilder.Draw += DrawUI;
		DalamudApi.PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;

		DalamudApi.Condition.ConditionChange += OnConditionChanged;

		Fools2023.Initialize(_configuration);
	}

	private Configuration LoadConfig()
	{
		var config = DalamudApi.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
		if (config.Version < 2)
		{
			config = new Configuration();
		}
		else if (config.Version == 2)
		{
			config.Version = 3;

			config.PositionalMissColor = config.PositionalColor;
			config.PositionalHitColor = config.PositionalColor;
			
			if (config.PositionalColorInvert)
			{
				config.PositionalMissColorEnabled = true;
				config.PositionalHitColorEnabled = false;
			}
			else
			{
				config.PositionalMissColorEnabled = false;
				config.PositionalHitColorEnabled = true;
			}
		}

		config.Initialize(this);
		config.Save();
		return config;
	}

	public void Dispose()
	{
		_actionStore.Dispose();
		ResetMainTargetCastBar();
		ResetFocusTargetCastBar();
		_receiveActionEffectHook?.Disable();
		_receiveActionEffectHook?.Dispose();
		_addScreenLogHook?.Disable();
		_addScreenLogHook?.Dispose();
		_setCastBarHook?.Disable();
		_setCastBarHook?.Dispose();
		_setFocusTargetCastBarHook?.Disable();
		_setFocusTargetCastBarHook?.Dispose();

		DalamudApi.FlyTextGui.FlyTextCreated -= OnFlyTextCreated;
		DalamudApi.Condition.ConditionChange -= OnConditionChanged;
		
		DalamudApi.PluginInterface.UiBuilder.Draw -= DrawUI;
		DalamudApi.PluginInterface.UiBuilder.OpenConfigUi -= DrawConfigUI;

		_actionStore = null;
		_actionToDamageTypeDict = null;

		_ui.Dispose();
		Fools2023.Dispose();
		DalamudApi.CommandManager.RemoveHandler(CommandName);
		DalamudApi.PluginInterface.RelinquishData("PetRenamer.GameObjectRenameDict");
	}
		
	private void OnCommand(string command, string args)
	{
		if (args == "fools2023" && !_configuration.Fools2023Config.Unlocked)
		{
			Fools2023.Unlock();
			var seStr = new SeStringBuilder()
				.AddUiForeground("[DamageInfoPlugin]", 506)
				.Add(new TextPayload(" New rare damage types"))
				.AddUiForeground(" UNLOCKED! ", 504)
				.Add(new TextPayload("You can type /dmginfo to open the settings and disable them if you prefer. Note that damage icons must be enabled in Damage Info to see them."))
				.Build();
			DalamudApi.ChatGui.Print(new XivChatEntry() { Message = seStr });
			return;
		}

		if (args == "posload")
		{
			_posManager.Reset();
			return;
		}
		
		_ui.SettingsVisible = true;
	}

	private void DrawUI()
	{
		_ui.Draw();
	}

	private void DrawConfigUI()
	{
		_ui.SettingsVisible = true;
	}
	
    private void OnConditionChanged(ConditionFlag flag, bool value) 
    {
        if (flag is not ConditionFlag.InCombat) return;

        // Combat has started
        if (value) 
        {
            _positionalsHit = 0;
            _positionalsAttempted = 0;
            _combatStartTime = DateTime.Now;
        }
        
        // Combat has ended
        else {
            if (_configuration.PositionalReportEnabled && _positionalsAttempted > 0) {
                var percentHit = (float) _positionalsHit / _positionalsAttempted * 100.0f;

                ushort color = percentHit switch 
                {
                    > 90f => 504,
                    > 80f => 506,
                    > 60f => 500,
                    <= 50f => 705,
                    _ => 1,
                };
            
                DalamudApi.ChatGui.Print(new XivChatEntry 
                {
                    Message = new SeStringBuilder()
                        .AddUiForeground("[DamageInfo] ", 506)
                        .AddUiForeground("[Positionals] ", 504)
                        .AddText($" {_positionalsHit} / {_positionalsAttempted} ( ")
                        .AddUiForeground($"{percentHit:F1}", color)
                        .AddText($"% ) ( {_combatStartTime - DateTime.Now:mm\\:ss} )")
                        .Build(),
                });
            }
        }
    }

#region castbar
	private CastbarInfo GetTargetInfoUiElements()
	{
		var unitBase = (AtkUnitBase*)DalamudApi.GameGui.GetAddonByName("_TargetInfo").Address;

		if (unitBase == null) return _nullCastbarInfo;

		return new CastbarInfo
		{
			unitBase = unitBase,
			gauge = unitBase->GetImageNodeById(TargetInfoGaugeNodeId),
			bg = unitBase->GetImageNodeById(TargetInfoGaugeBgNodeId),
		};
	}

	private CastbarInfo GetTargetInfoSplitUiElements()
	{
		var unitBase = (AtkUnitBase*)DalamudApi.GameGui.GetAddonByName("_TargetInfoCastBar").Address;

		if (unitBase == null) return _nullCastbarInfo;

		return new CastbarInfo
		{
			unitBase = unitBase,
			gauge = unitBase->GetImageNodeById(TargetInfoSplitGaugeNodeId),
			bg = unitBase->GetImageNodeById(TargetInfoSplitGaugeBgNodeId),
		};
	}

	private CastbarInfo GetFocusTargetUiElements()
	{
		var unitBase = (AtkUnitBase*)DalamudApi.GameGui.GetAddonByName("_FocusTargetInfo").Address;

		if (unitBase == null) return _nullCastbarInfo;

		return new CastbarInfo
		{
			unitBase = unitBase,
			gauge = unitBase->GetImageNodeById(FocusTargetInfoGaugeNodeId),
			bg = unitBase->GetImageNodeById(FocusTargetInfoGaugeBgNodeId),
		};
	}

	public void ResetMainTargetCastBar()
	{
		GetTargetInfoUiElements().ResetIfValid();
		GetTargetInfoSplitUiElements().ResetIfValid();
	}

	public void ResetFocusTargetCastBar()
	{
		GetFocusTargetUiElements().ResetIfValid();
	}

	private void SetCastBarDetour(nint thisPtr, nint a2, nint a3, nint a4, char a5)
	{
		if (!_configuration.MainTargetCastBarColorEnabled)
		{
			_setCastBarHook.Original(thisPtr, a2, a3, a4, a5);
			return;
		}

		var targetInfo = GetTargetInfoUiElements();
		var splitInfo = GetTargetInfoSplitUiElements();

		if (!targetInfo.Valid() && !splitInfo.Valid())
		{
			_setCastBarHook.Original(thisPtr, a2, a3, a4, a5);
			return;
		}

		var toColor = _nullCastbarInfo;
		if (thisPtr.ToPointer() == targetInfo.unitBase)
			toColor = targetInfo;
		else if (thisPtr.ToPointer() == splitInfo.unitBase)
			toColor = splitInfo;

		if (toColor != _nullCastbarInfo)
			ColorCastBar(DalamudApi.TargetManager.Target, toColor, _setCastBarHook, thisPtr, a2, a3, a4, a5);
	}

	private void SetFocusTargetCastBarDetour(nint thisPtr, nint a2, nint a3, nint a4, char a5)
	{
		if (!_configuration.FocusTargetCastBarColorEnabled)
		{
			_setFocusTargetCastBarHook.Original(thisPtr, a2, a3, a4, a5);
			return;
		}

		var ftInfo = GetFocusTargetUiElements();

		if (thisPtr.ToPointer() != ftInfo.unitBase || !ftInfo.Valid()) return;

		ColorCastBar(DalamudApi.TargetManager.FocusTarget, ftInfo, _setFocusTargetCastBarHook, thisPtr, a2, a3, a4, a5);
	}

	private void ColorCastBar(IGameObject target, CastbarInfo info, Hook<SetCastBarDelegate> hook, nint thisPtr, nint a2, nint a3, nint a4, char a5)
	{
		if (target == null || target is not IBattleChara battleTarget)
		{
			hook.Original(thisPtr, a2, a3, a4, a5);
			return;
		}

		var actionId = battleTarget.CastActionId;
		_actionToDamageTypeDict.TryGetValue(actionId, out var type);
		// DebugLog(Castbar, $"casting {actionId} {type}");
		if (_ignoredCastActions.Contains(actionId))
		{
			info.Reset();
			hook.Original(thisPtr, a2, a3, a4, a5);
			return;
		}

		var castColor = type switch
		{
			DamageType.Physical => _configuration.PhysicalCastColor,
			DamageType.Magical => _configuration.MagicCastColor,
			DamageType.Unique => _configuration.DarknessCastColor,
			_ => Vector4.One,
		};

		var bgColor = type switch
		{
			DamageType.Physical => _configuration.PhysicalBgColor,
			DamageType.Magical => _configuration.MagicBgColor,
			DamageType.Unique => _configuration.DarknessBgColor,
			_ => Vector4.One,
		};

		info.Color(castColor, bgColor);
		hook.Original(thisPtr, a2, a3, a4, a5);
	}
#endregion

	private List<uint> FindCharaPets()
	{
		var results = new List<uint>();
		var charaId = GetCharacterActorId();
		foreach (var obj in DalamudApi.ObjectTable)
		{
			if (obj is not IBattleNpc npc) continue;

			var actPtr = npc.Address;
			if (actPtr == IntPtr.Zero) continue;

			if (npc.OwnerId == charaId)
				results.Add(npc.EntityId);
		}

		return results;
	}

	private uint GetCharacterActorId()
	{
		return DalamudApi.ObjectTable.LocalPlayer?.EntityId ?? 0;
	}

	private SeString GetActorName(uint id)
	{
		var dGameObject = DalamudApi.ObjectTable.SearchById(id);
		if (dGameObject == null) return SeString.Empty;
		if (_petNicknamesDictionary != null)
		{
			if (dGameObject.ObjectKind == DObjectKind.BattleNpc && _petNicknamesDictionary.TryGetValue(dGameObject.GameObjectId, out var name)) return name;
		}
		return dGameObject.Name;
    }

	private void ReceiveActionEffect(
		uint sourceId,
		Character* sourceCharacter,
		Vector3* targetPos,
		ActionEffectHandler.Header* effectHeader,
		ActionEffectHandler.TargetEffects* targetEffects,
		GameObjectId* targetEntityIds)
	{
		try
		{
			_actionStore.Cleanup();
			if (effectHeader is null || targetEffects is null || targetEntityIds is null)
				return;

			var targetCount = Math.Min((int)effectHeader->NumTargets, 32);
			var actionId = (int)effectHeader->ActionId;
			DebugLog(Effect, $"--- source actor={sourceId:X8} action={actionId} targets={targetCount} ---");

			var positionalState = PositionalState.Ignore;
			var isPositional = _posManager.IsPositional(actionId);
			if (isPositional)
			{
				positionalState = PositionalState.Failure;
				for (var targetIndex = 0; targetIndex < targetCount; targetIndex++)
				for (var effectIndex = 0; effectIndex < 8; effectIndex++)
				{
					ref var effect = ref targetEffects[targetIndex].Effects[effectIndex];
					if ((ActionEffectType)effect.Type == ActionEffectType.Damage &&
						_posManager.IsPositionalHit(actionId, effect.Param2))
					{
						positionalState = PositionalState.Success;
					}
				}

				if (DalamudApi.ObjectTable.LocalPlayer?.EntityId == sourceId)
				{
					if (positionalState is PositionalState.Success)
						_positionalsHit++;
					_positionalsAttempted++;
				}
			}

			for (var targetIndex = 0; targetIndex < targetCount; targetIndex++)
			{
				var target = GetEntityId(targetEntityIds[targetIndex]);
				for (var effectIndex = 0; effectIndex < 8; effectIndex++)
				{
					ref var effect = ref targetEffects[targetIndex].Effects[effectIndex];
					var effectType = (ActionEffectType)effect.Type;
					if (effectType == ActionEffectType.Nothing)
						continue;

					if (isPositional && effectType == ActionEffectType.Damage && sourceId == GetCharacterActorId())
					{
						var actionName = _actionToNameDict.TryGetValue((uint)actionId, out var sheetName)
							? $"{sheetName} [{actionId}]"
							: actionId.ToString();
						PositionalLog($"Action: {actionName} jobLevel: {GetCurrentLevel()} boostPercent: {effect.Param2} positionalState: {positionalState}");
					}

					uint damage = effect.Value;
					if ((effect.Param4 & 0x40) == 0x40)
						damage += (uint)effect.Param3 << 16;

					var damageType = ((AttackType)(effect.Param1 & 0xF)).ToDamageType();
					if (effectType == ActionEffectType.Heal)
						damageType = DamageType.None;

					MitigationResult? mitigation = null;
					if (_configuration.IncomingMitigationEnabled &&
						target == GetCharacterActorId() &&
						sourceId != GetCharacterActorId() &&
						effectType is ActionEffectType.Damage or ActionEffectType.BlockedDamage or ActionEffectType.ParriedDamage)
					{
						mitigation = MitigationCalculator.Calculate(
							damageType,
							CaptureMitigationStatuses(target),
							CaptureMitigationStatuses(sourceId),
							_configuration.MitigationIncludeSourceDebuffs);
						MitigationLog($"capture action={effectHeader->ActionId} source={sourceId:X8} target={target:X8} amount={damage} type={damageType} rate={mitigation.DisplayPercent} rules={mitigation.Contributions.Count}");
					}

					_actionStore.AddEffect(new ActionEffectInfo
					{
						step = ActionStep.Effect,
						actionId = effectHeader->ActionId,
						type = effectType,
						damageType = damageType,
						sourceId = sourceId,
						targetId = target,
						value = damage,
						positionalState = positionalState,
						mitigation = mitigation,
					});
				}
			}
		}
		catch (Exception e)
		{
			DalamudApi.PluginLog.Error(e, "An error has occurred in Damage Info Extended.");
		}

		_receiveActionEffectHook.Original(sourceId, sourceCharacter, targetPos, effectHeader, targetEffects, targetEntityIds);
	}

	private int GetCurrentLevel()
	{
		return DalamudApi.ObjectTable.LocalPlayer?.Level ?? -1;
	}

	private void AddScreenLogDetour(
		Character* target,
		Character* source,
		FlyTextKind logKind,
		int option,
		int actionKind,
		int actionId,
		int val1,
		int val2,
		int serverAttackType,
		int val4)
	{
		try
		{
			var targetId = target->GameObject.EntityId;
			var sourceId = source->GameObject.EntityId;

			if (_configuration.DebugLogEnabled)
			{
				DebugLog(ScreenLog, $"{option} {actionKind} {actionId}");
				DebugLog(ScreenLog, $"{val1} {val2} {serverAttackType} {val4}");
				var targetName = GetActorName(targetId);
				var sourceName = GetActorName(sourceId);
				DebugLog(ScreenLog, $"src {sourceId} {sourceName}");
				DebugLog(ScreenLog, $"tgt {targetId} {targetName}");
			}

			_actionStore.UpdateEffect((uint)actionId, sourceId, targetId, (uint)val1, (uint)serverAttackType, logKind);
			MitigationLog($"screenlog action={actionId} source={sourceId:X8} target={targetId:X8} amount={val1} serverType={serverAttackType} kind={(int)logKind}");
		}
		catch (Exception e)
		{
			DalamudApi.PluginLog.Error(e, "An error occurred in Damage Info.");
		}

		_addScreenLogHook.Original(target, source, logKind, option, actionKind, actionId, val1, val2, serverAttackType, val4);
	}

	private void OnFlyTextCreated(
		ref FlyTextKind kind,
		ref int val1,
		ref int val2,
		ref SeString text1,
		ref SeString text2,
		ref uint color,
		ref uint icon,
		ref uint damageTypeIcon,
		ref float yOffset,
		ref bool handled)
	{
		try
		{
			var ftKind = kind;

			if (_configuration.DebugLogEnabled)
			{
				var str1 = text1?.TextValue.Replace("%", "%%");
				var str2 = text2?.TextValue.Replace("%", "%%");

				DebugLog(FlyText, $"flytext created: kind: {ftKind} ({(int)kind}), val1: {val1}, val2: {val2}, color: {color:X}, icon: {icon}");
				DebugLog(FlyText, $"text1: {str1} | text2: {str2}");
			}

			var charaId = GetCharacterActorId();
			var petIds = FindCharaPets();

			var damageType = ((SeDamageType)damageTypeIcon).ToDamageType();
			if (!_actionStore.TryGetEffect((uint)val1, damageType, ftKind, charaId, petIds, out var info))
			{
				MitigationLog($"flytext no-match amount={val1} type={damageType} kind={(int)ftKind}");
				DebugLog(FlyText, $"Failed to obtain info... {val1} {damageType} {ftKind} {charaId}");
				return;
			}

			DebugLog(FlyText, $"Obtained info: {info}");
			
			SeCheck(info, (SeDamageType)damageTypeIcon, damageType);
			
			// I'd like to color dodges, so let's fallback in the case that we have a dodge - SE doesn't send info on these
			if (info is { value: 0, kind: FlyTextKind.Miss or FlyTextKind.NamedMiss } && _actionToDamageTypeDict.TryGetValue(info.actionId, out damageType))
			{
				DebugLog(FlyText, $"Processed fallback actionId {info.actionId} to {damageType} added icon {damageTypeIcon}");
			}

			var isHealingAction = info.type == ActionEffectType.Heal;
			var isPetAction = petIds.Contains(info.sourceId);
			var isCharaAction = info.sourceId == charaId;
			var isCharaTarget = info.targetId == charaId;

			if ((_configuration.IncomingColorEnabled || _configuration.OutgoingColorEnabled || _configuration.PositionalHitColorEnabled || _configuration.PositionalMissColorEnabled))
			{
				var incomingCheck = !isCharaAction && isCharaTarget && !isHealingAction && _configuration.IncomingColorEnabled;
				var outgoingCheck = isCharaAction && !isCharaTarget && !isHealingAction && _configuration.OutgoingColorEnabled;
				var petCheck = !isCharaAction && !isCharaTarget && petIds.Contains(info.sourceId) && !isHealingAction && _configuration.PetColorEnabled;

				// Large check - check that it's a character action, we shouldn't ignore the state, and that positionals are enabled
				// then, check to see if we should color the success or the failure
				var posCheck = isCharaAction && info.positionalState != PositionalState.Ignore;

				if (incomingCheck || outgoingCheck || petCheck)
					color = GetDamageColor(damageType);

				if (posCheck)
				{
					color = info.positionalState switch
					{
						PositionalState.Success when _configuration.PositionalHitColorEnabled => ImGui.GetColorU32(_configuration.PositionalHitColor),
						PositionalState.Failure when _configuration.PositionalMissColorEnabled => ImGui.GetColorU32(_configuration.PositionalMissColor),
						_ => color,
					};
				}
			}

			var isIncomingDamage = _configuration.IncomingMitigationEnabled &&
				!isCharaAction &&
				isCharaTarget &&
				!isHealingAction;
			var incomingMitigation = isIncomingDamage &&
				info.mitigation is { HasKnownReduction: true } mitigation
				? mitigation
				: null;
			var statusReduction = incomingMitigation?.Reduction ?? 0f;
			var nativeBlockParryReduction = 0f;
			var originalSubtitle = text2 ?? SeString.Empty;
			var subtitleWithoutNativeBlockParry = originalSubtitle;
			if (isIncomingDamage && IncomingFlyTextFormatter.TryExtractBlockOrParry(originalSubtitle.TextValue, out var nativeReduction, out var strippedSubtitle))
			{
				nativeBlockParryReduction = nativeReduction;
				subtitleWithoutNativeBlockParry = strippedSubtitle.Length == 0
					? SeString.Empty
					: new SeString(new List<Payload> { new TextPayload(strippedSubtitle) });
			}

			var combinedReduction = IncomingFlyTextFormatter.CombineReductions(statusReduction, nativeBlockParryReduction);
			var incomingSuffix = IncomingFlyTextFormatter.BuildSourceSuffix(combinedReduction);

			if (_configuration.SourceTextEnabled || _configuration.PetSourceTextEnabled || _configuration.HealSourceTextEnabled || !string.IsNullOrEmpty(incomingSuffix))
			{
				var tgtCheck = !isCharaAction && !isHealingAction && !isPetAction && _configuration.SourceTextEnabled;
				var petCheck = isPetAction && _configuration.PetSourceTextEnabled;
				var healCheck = isHealingAction && _configuration.HealSourceTextEnabled;

				if ((tgtCheck || petCheck || healCheck || !string.IsNullOrEmpty(incomingSuffix)) && GetActorName(info.sourceId).Payloads.Count > 0)
				{
					text2 = GetNewText(info.sourceId, subtitleWithoutNativeBlockParry, incomingSuffix, _configuration.MitigationTextBeforeSource);
				}
			}

			if (incomingMitigation is not null || nativeBlockParryReduction > 0)
			{
				MitigationLog($"flytext matched action={info.actionId} source={info.sourceId:X8} target={info.targetId:X8} amount={info.value} type={info.damageType} statusRate={statusReduction * 100:0.#}% nativeBlockParry={nativeBlockParryReduction * 100:0.#}% totalRate={combinedReduction * 100:0.#}%");
			}

			if (_configuration.SeDamageIconDisable)
				damageTypeIcon = 0;

			if (info.type == ActionEffectType.Damage)
				Fools2023.SetRareDamageType(ref damageTypeIcon, ref text1);
			
			// Attack text checks
			if (!_configuration.IncomingAttackTextEnabled
			    || !_configuration.OutgoingAttackTextEnabled
			    || !_configuration.PetAttackTextEnabled
			    || !_configuration.HealAttackTextEnabled
			    || _configuration.AnyPositionalTextEnabled())
			{
				var incomingCheck = !isCharaAction && isCharaTarget && !isHealingAction && !isPetAction && !_configuration.IncomingAttackTextEnabled;
				var outgoingCheck = isCharaAction && !isCharaTarget && !isHealingAction && !isPetAction && !_configuration.OutgoingAttackTextEnabled;
				var petCheck = !isCharaAction && !isCharaTarget && !isHealingAction && isPetAction && !_configuration.PetAttackTextEnabled;
				var healCheck = isHealingAction && !isPetAction && !_configuration.HealAttackTextEnabled;

				var hitCheck = _configuration.PositionalHitTextSettings.AnyEnabled() && info.positionalState == PositionalState.Success;
				var missCheck = _configuration.PositionalMissTextSettings.AnyEnabled() && info.positionalState == PositionalState.Failure;
				var posAnyCheck = hitCheck || missCheck;

				var posOverride = (_configuration.PositionalAttackTextOverrideEnabled && !_configuration.OutgoingAttackTextEnabled)
				                  || _configuration.OutgoingAttackTextEnabled;
				var posCheck = posAnyCheck && posOverride && info.positionalState != PositionalState.Ignore;

				if (incomingCheck || petCheck || healCheck || (outgoingCheck && !posCheck))
					text1 = "";

				if (posCheck)
				{
					var payloads = new List<Payload>();
					if (hitCheck && _configuration.PositionalHitTextSettings.IsPrefixEnabled())
						payloads.Add(_configuration.PositionalHitTextSettings.PrefixPayload());
					if (missCheck && _configuration.PositionalMissTextSettings.IsPrefixEnabled())
						payloads.Add(_configuration.PositionalMissTextSettings.PrefixPayload());
					payloads.AddRange(text1.Payloads);
					if (hitCheck && _configuration.PositionalHitTextSettings.IsSuffixEnabled())
						payloads.Add(_configuration.PositionalHitTextSettings.SuffixPayload());
					if (missCheck && _configuration.PositionalMissTextSettings.IsSuffixEnabled())
						payloads.Add(_configuration.PositionalMissTextSettings.SuffixPayload());
					text1.Payloads.Clear();
					text1.Payloads.AddRange(payloads);
				}
			}

			if (_configuration.AnyPositionalSoundEnabled())
			{
				var hitSettings = _configuration.PositionalHitSoundSettings;
				var missSettings = _configuration.PositionalMissSoundSettings;
				if (info.positionalState == PositionalState.Success && hitSettings.Enabled)
					PlaySE(hitSettings.SoundId);
				if (info.positionalState == PositionalState.Failure && missSettings.Enabled)
					PlaySE(missSettings.SoundId);
			}
		}
		catch (Exception e)
		{
			DalamudApi.PluginLog.Error(e, "An error has occurred in Damage Info");
		}
	}

	private void SeCheck(ActionEffectInfo info, SeDamageType seDamageType, DamageType dmgType)
	{
		if ((seDamageType == SeDamageType.Physical && dmgType != DamageType.Physical) ||
		    (seDamageType == SeDamageType.Magical && dmgType != DamageType.Magical) ||
		    (seDamageType == SeDamageType.Unique && dmgType != DamageType.Unique))
		{
			var warning = $"Encountered a damage type mismatch on {info.actionId}: SE says {seDamageType}, damage info says {dmgType}";
			DalamudApi.PluginLog.Information(warning);
				
#if DEBUG
			var seStr = new SeStringBuilder()
				.AddUiForeground("[DamageInfoPlugin]", 506)
				.Add(new TextPayload($" {warning}."))
				.AddUiForeground(" Please report this in the Damage Info thread in the Goat Place discord!", 60)
				.Build();
			DalamudApi.ChatGui.Print(new XivChatEntry() { Message = seStr });
#endif
		}
	}
	
	private void PositionalLog(string message)
	{
		if (!_configuration.PositionalLogEnabled) return;
		var seStr = new SeStringBuilder()
			.AddUiForeground("[DamageInfoPlugin]", 506)
			.Add(new TextPayload($" {message}."))
			.Build();
		DalamudApi.ChatGui.Print(new XivChatEntry { Message = seStr });
	}

	private void PlaySE(int soundId)
	{
		try
		{
			UIGlobals.PlayChatSoundEffect((uint)soundId);
		}
		catch (ArgumentException e)
		{
			DebugLog(Sound, $"Failed to play sound {soundId}: {e.Message}");
		}
	}

	private SeString GetNewText(uint sourceId, SeString originalText, string? suffix = null, bool suffixBeforeSource = false)
	{
		SeString name = GetActorName(sourceId);
		var newPayloads = new List<Payload>();

		if (name.Payloads.Count == 0) return originalText;
		if (suffixBeforeSource && !string.IsNullOrEmpty(suffix))
		{
			newPayloads.Add(new TextPayload(suffix.TrimStart()));
			newPayloads.Add(new TextPayload(" "));
		}

		switch (DalamudApi.ClientState.ClientLanguage)
		{
			case ClientLanguage.Japanese:
				newPayloads.AddRange(name.Payloads);
				newPayloads.Add(new TextPayload("から"));
				break;
			case ClientLanguage.English:
				newPayloads.Add(new TextPayload("from "));
				newPayloads.AddRange(name.Payloads);
				break;
			case ClientLanguage.German:
				newPayloads.Add(new TextPayload("von "));
				newPayloads.AddRange(name.Payloads);
				break;
			case ClientLanguage.French:
				newPayloads.Add(new TextPayload("de "));
				newPayloads.AddRange(name.Payloads);
				break;
		default:
			newPayloads.Add(new TextPayload(">"));
			newPayloads.AddRange(name.Payloads);
			break;
		}

		if (!suffixBeforeSource && !string.IsNullOrEmpty(suffix))
			newPayloads.Add(new TextPayload(suffix));

		if (originalText.Payloads.Count > 0)
			newPayloads.AddRange(originalText.Payloads);

		return new SeString(newPayloads);
	}

	private IReadOnlyList<MitigationStatus> CaptureMitigationStatuses(uint entityId)
	{
		if (entityId == 0 || DalamudApi.ObjectTable.SearchById(entityId) is not IBattleChara battleChara)
			return Array.Empty<MitigationStatus>();

		var sheet = DalamudApi.DataManager.GetExcelSheet<Status>();
		var results = new List<MitigationStatus>();
		foreach (var status in battleChara.StatusList)
		{
			if (status.StatusId == 0)
				continue;

			var name = sheet?.GetRowOrDefault(status.StatusId)?.Name.ExtractText() ?? string.Empty;
			results.Add(new MitigationStatus(status.StatusId, name, status.SourceId));
		}

		return results;
	}

	private static uint GetEntityId(GameObjectId targetId)
		=> targetId.ObjectId != 0
			? targetId.ObjectId
			: targetId.Id <= uint.MaxValue ? (uint)targetId.Id : 0;

	private void DebugLog(LogType type, string str)
	{
		if (_configuration.DebugLogEnabled)
			DalamudApi.PluginLog.Information($"[{type}] {str}");
	}

	private void MitigationLog(string message)
	{
		if (_configuration.MitigationDiagnosticsEnabled)
			DalamudApi.PluginLog.Information($"[Mitigation] {message}");
	}

	private uint GetDamageColor(DamageType type, uint fallback = 0xFF00008A)
	{
		return type switch
		{
			DamageType.Physical => ImGui.GetColorU32(_configuration.PhysicalColor),
			DamageType.Magical => ImGui.GetColorU32(_configuration.MagicColor),
			DamageType.Unique => ImGui.GetColorU32(_configuration.DarknessColor),
			_ => fallback
		};
	}
}
