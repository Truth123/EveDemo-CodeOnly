// 文件说明：定义 Combat Timeline 运行时资产、Clip 类型、查询转换和校验逻辑。
// 所属模块：Combat Timeline。
// 运行影响：影响 Player/Boss 动作窗口、HitNode、Reaction 和后续能力快照解析。

using ProjectEVE.Boss.AI;
using ProjectEVE.Player.Attacks;
using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ProjectEVE.Combat.Timeline
{
    public static class CombatTimelineProvider
    {
        private const string ResourcesCatalogPath = "CombatTimelines";
        private static readonly Dictionary<string, CombatTimelineActionAsset> timelineById = new Dictionary<string, CombatTimelineActionAsset>();
        private static readonly Dictionary<string, AttackDefinition> playerAttackById = new Dictionary<string, AttackDefinition>();
        private static readonly Dictionary<string, PlayerSkillActionData> playerSkillById = new Dictionary<string, PlayerSkillActionData>();
        private static readonly Dictionary<string, PlayerEvadeActionData> playerEvadeById = new Dictionary<string, PlayerEvadeActionData>();
        private static readonly Dictionary<string, PlayerGuardActionData> playerGuardById = new Dictionary<string, PlayerGuardActionData>();
        private static readonly Dictionary<string, CombatTimelineReactionConfig> playerReactionById = new Dictionary<string, CombatTimelineReactionConfig>();
        private static readonly Dictionary<string, BossAttackDefinition> bossAttackById = new Dictionary<string, BossAttackDefinition>();
        private static readonly Dictionary<string, CombatTimelineReactionConfig> bossReactionById = new Dictionary<string, CombatTimelineReactionConfig>();
        private static bool initialized;

        public static bool IsUsingAssets
        {
            get
            {
                EnsureInitialized();
                return timelineById.Count > 0;
            }
        }

        /// <summary>
        /// 重置 Cache 到默认状态，通常用于重新开始测试或恢复 Inspector 初值。
        /// </summary>
        public static void ResetCache()
        {
            initialized = false;
            timelineById.Clear();
            ResetRuntimeDataCache();
        }

        /// <summary>
        /// 清除已转换的运行时数据，保留已加载的 Timeline 资产索引。
        /// </summary>
        public static void ResetRuntimeDataCache()
        {
            playerAttackById.Clear();
            playerSkillById.Clear();
            playerEvadeById.Clear();
            playerGuardById.Clear();
            playerReactionById.Clear();
            bossAttackById.Clear();
            bossReactionById.Clear();
        }

        /// <summary>
        /// 预热所有已加载 Timeline 的运行时数据，用于编辑器手动刷新后让运行时读取新配置。
        /// </summary>
        public static void RebuildRuntimeDataCache()
        {
            EnsureInitialized();
            ResetRuntimeDataCache();

            foreach (CombatTimelineActionAsset timeline in timelineById.Values)
            {
                CacheRuntimeData(timeline);
            }
        }

        /// <summary>
        /// 尝试执行 Get / Timeline，返回是否成功，并避免在失败路径产生不必要的状态提交。
        /// </summary>
        public static bool TryGetTimeline(string actionId, out CombatTimelineActionAsset timeline)
        {
            EnsureInitialized();
            if (!string.IsNullOrEmpty(actionId) && timelineById.TryGetValue(actionId, out timeline) && timeline != null)
            {
                return true;
            }

            timeline = null;
            return false;
        }

        /// <summary>
        /// 尝试执行 Get / Player / Attack，返回是否成功，并避免在失败路径产生不必要的状态提交。
        /// </summary>
        public static bool TryGetPlayerAttack(string nodeId, out AttackDefinition definition)
        {
            if (TryGetTimeline(nodeId, out CombatTimelineActionAsset timeline) &&
                timeline is PlayerAttackTimelineAsset playerAttack)
            {
                return TryGetOrCreatePlayerAttack(playerAttack, out definition);
            }

            definition = null;
            return false;
        }


        /// <summary>
        /// 尝试执行 Get / First / Player / Attack，返回是否成功，并避免在失败路径产生不必要的状态提交。
        /// </summary>
        public static bool TryGetFirstPlayerAttack(AttackInputType inputType, out AttackDefinition definition)
        {
            EnsureInitialized();
            PlayerAttackTimelineAsset firstAttack = null;
            foreach (CombatTimelineActionAsset timeline in timelineById.Values)
            {
                if (timeline is not PlayerAttackTimelineAsset playerAttack ||
                    playerAttack.AttackInputType != inputType ||
                    playerAttack.ComboIndex != 1)
                {
                    continue;
                }

                if (firstAttack != null)
                {
                    Debug.LogError($"Multiple PlayerAttack Timeline assets found for first node {inputType} ComboIndex=1. Resolve duplicated data before entering Attack state.");
                    definition = null;
                    return false;
                }

                firstAttack = playerAttack;
            }

            if (firstAttack != null)
            {
                return TryGetOrCreatePlayerAttack(firstAttack, out definition);
            }

            definition = null;
            return false;
        }

        /// <summary>
        /// 尝试执行 Get / Player / Skill，返回状态进入时使用的动作数据。
        /// </summary>
        public static bool TryGetPlayerSkill(string skillId, out PlayerSkillActionData data)
        {
            if (TryGetTimeline(skillId, out CombatTimelineActionAsset timeline) &&
                timeline is PlayerSkillTimelineAsset playerSkill)
            {
                return TryGetOrCreatePlayerSkill(playerSkill, out data);
            }

            data = null;
            return false;
        }

        /// <summary>
        /// 尝试执行 Get / Player / Evade，返回状态进入时使用的动作数据。
        /// </summary>
        public static bool TryGetPlayerEvade(string evadeId, out PlayerEvadeActionData data)
        {
            if (TryGetTimeline(evadeId, out CombatTimelineActionAsset timeline) &&
                timeline is PlayerEvadeTimelineAsset playerEvade)
            {
                return TryGetOrCreatePlayerEvade(playerEvade, out data);
            }

            data = null;
            return false;
        }

        /// <summary>
        /// 尝试执行 Get / Player / Guard，返回状态进入时使用的动作数据。
        /// </summary>
        public static bool TryGetPlayerGuard(string guardId, out PlayerGuardActionData data)
        {
            if (TryGetTimeline(guardId, out CombatTimelineActionAsset timeline) &&
                timeline is PlayerGuardTimelineAsset playerGuard)
            {
                return TryGetOrCreatePlayerGuard(playerGuard, out data);
            }

            data = null;
            return false;
        }

        /// <summary>
        /// 尝试执行 Get / Boss / Attack，返回是否成功，并避免在失败路径产生不必要的状态提交。
        /// </summary>
        public static bool TryGetBossAttack(string attackId, out BossAttackDefinition definition)
        {
            if (TryGetTimeline(attackId, out CombatTimelineActionAsset timeline) &&
                timeline is BossAttackTimelineAsset bossAttack)
            {
                return TryGetOrCreateBossAttack(bossAttack, out definition);
            }

            definition = null;
            return false;
        }

        /// <summary>
        /// 尝试执行 Get / Player / Reaction，返回是否成功，并避免在失败路径产生不必要的状态提交。
        /// </summary>
        public static bool TryGetPlayerReaction(string reactionId, out CombatTimelineReactionConfig config)
        {
            if (TryGetTimeline(reactionId, out CombatTimelineActionAsset timeline) &&
                timeline is PlayerReactionTimelineAsset playerReaction)
            {
                return TryGetOrCreatePlayerReaction(playerReaction, out config);
            }

            config = default;
            return false;
        }

        /// <summary>
        /// 尝试执行 Get / Boss / Reaction，返回是否成功，并避免在失败路径产生不必要的状态提交。
        /// </summary>
        public static bool TryGetBossReaction(string reactionId, out CombatTimelineReactionConfig config)
        {
            if (TryGetTimeline(reactionId, out CombatTimelineActionAsset timeline) &&
                timeline is BossReactionTimelineAsset bossReaction)
            {
                return TryGetOrCreateBossReaction(bossReaction, out config);
            }

            config = default;
            return false;
        }

        /// <summary>
        /// 执行 Enumerate / Boss / Attacks 相关逻辑，并维护 Combat Timeline 模块的运行时一致性。
        /// </summary>
        public static IEnumerable<BossAttackDefinition> EnumerateBossAttacks()
        {
            EnsureInitialized();
            foreach (CombatTimelineActionAsset timeline in timelineById.Values)
            {
                if (timeline is BossAttackTimelineAsset bossAttack)
                {
                    if (TryGetOrCreateBossAttack(bossAttack, out BossAttackDefinition definition))
                    {
                        yield return definition;
                    }
                }
            }
        }

        /// <summary>
        /// 确保 Initialized 可用，不满足时创建、刷新或钳制必要的运行时数据。
        /// </summary>
        private static void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            timelineById.Clear();

            LoadFromResources();

#if UNITY_EDITOR
            LoadFromAssetDatabase();
#endif
        }

        /// <summary>
        /// 加载 From / Resources 资源或配置，并注册到运行时查询入口。
        /// </summary>
        private static void LoadFromResources()
        {
            CombatTimelineCatalog[] catalogs = Resources.LoadAll<CombatTimelineCatalog>(ResourcesCatalogPath);
            for (int i = 0; i < catalogs.Length; i++)
            {
                RegisterCatalog(catalogs[i]);
            }

            CombatTimelineActionAsset[] looseTimelines = Resources.LoadAll<CombatTimelineActionAsset>(ResourcesCatalogPath);
            for (int i = 0; i < looseTimelines.Length; i++)
            {
                RegisterTimeline(looseTimelines[i]);
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// 加载 From / Asset / Database 资源或配置，并注册到运行时查询入口。
        /// </summary>
        private static void LoadFromAssetDatabase()
        {
            HashSet<string> guids = new HashSet<string>();
            AddAssetGuids("t:CombatTimelineCatalog", guids);
            AddTimelineAssetGuids(guids);

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                CombatTimelineCatalog catalog = AssetDatabase.LoadAssetAtPath<CombatTimelineCatalog>(path);
                if (catalog != null)
                {
                    RegisterCatalog(catalog);
                    continue;
                }

                RegisterTimeline(AssetDatabase.LoadAssetAtPath<CombatTimelineActionAsset>(path));
            }
        }

        /// <summary>
        /// 添加 Asset / Guids 数据，并维护集合、缓存或运行时状态的一致性。
        /// </summary>
        private static void AddAssetGuids(string filter, HashSet<string> destination)
        {
            string[] foundGuids = AssetDatabase.FindAssets(filter, new[] { "Assets/ScriptableObjects/CombatTimelines" });
            for (int i = 0; i < foundGuids.Length; i++)
            {
                destination.Add(foundGuids[i]);
            }
        }

        /// <summary>
        /// 添加 Timeline / Asset / Guids 数据，并维护集合、缓存或运行时状态的一致性。
        /// </summary>
        private static void AddTimelineAssetGuids(HashSet<string> destination)
        {
            AddAssetGuids("t:PlayerAttackTimelineAsset", destination);
            AddAssetGuids("t:PlayerSkillTimelineAsset", destination);
            AddAssetGuids("t:PlayerEvadeTimelineAsset", destination);
            AddAssetGuids("t:PlayerGuardTimelineAsset", destination);
            AddAssetGuids("t:PlayerReactionTimelineAsset", destination);
            AddAssetGuids("t:BossAttackTimelineAsset", destination);
            AddAssetGuids("t:BossReactionTimelineAsset", destination);
        }
#endif

        /// <summary>
        /// 注册 Catalog 到查询表、事件或运行时缓存中。
        /// </summary>
        private static void RegisterCatalog(CombatTimelineCatalog catalog)
        {
            if (catalog?.Timelines == null)
            {
                return;
            }

            for (int i = 0; i < catalog.Timelines.Length; i++)
            {
                RegisterTimeline(catalog.Timelines[i]);
            }
        }

        /// <summary>
        /// 注册 Timeline 到查询表、事件或运行时缓存中。
        /// </summary>
        private static void RegisterTimeline(CombatTimelineActionAsset timeline)
        {
            if (timeline == null || string.IsNullOrEmpty(timeline.ActionId))
            {
                return;
            }

            // Last registration wins so loose live assets can override catalog entries during editor iteration.
            timelineById[timeline.ActionId] = timeline;
            RemoveRuntimeData(timeline.ActionId);
        }

        private static void CacheRuntimeData(CombatTimelineActionAsset timeline)
        {
            switch (timeline)
            {
                case PlayerAttackTimelineAsset playerAttack:
                    TryGetOrCreatePlayerAttack(playerAttack, out _);
                    break;
                case PlayerSkillTimelineAsset playerSkill:
                    TryGetOrCreatePlayerSkill(playerSkill, out _);
                    break;
                case PlayerEvadeTimelineAsset playerEvade:
                    TryGetOrCreatePlayerEvade(playerEvade, out _);
                    break;
                case PlayerGuardTimelineAsset playerGuard:
                    TryGetOrCreatePlayerGuard(playerGuard, out _);
                    break;
                case PlayerReactionTimelineAsset playerReaction:
                    TryGetOrCreatePlayerReaction(playerReaction, out _);
                    break;
                case BossAttackTimelineAsset bossAttack:
                    TryGetOrCreateBossAttack(bossAttack, out _);
                    break;
                case BossReactionTimelineAsset bossReaction:
                    TryGetOrCreateBossReaction(bossReaction, out _);
                    break;
            }
        }

        private static void RemoveRuntimeData(string actionId)
        {
            playerAttackById.Remove(actionId);
            playerSkillById.Remove(actionId);
            playerEvadeById.Remove(actionId);
            playerGuardById.Remove(actionId);
            playerReactionById.Remove(actionId);
            bossAttackById.Remove(actionId);
            bossReactionById.Remove(actionId);
        }

        private static bool TryGetOrCreatePlayerAttack(PlayerAttackTimelineAsset timeline, out AttackDefinition definition)
        {
            if (playerAttackById.TryGetValue(timeline.ActionId, out definition) && definition != null)
            {
                return true;
            }

            definition = timeline.ToAttackDefinition();
            playerAttackById[timeline.ActionId] = definition;
            return true;
        }

        private static bool TryGetOrCreatePlayerSkill(PlayerSkillTimelineAsset timeline, out PlayerSkillActionData data)
        {
            if (playerSkillById.TryGetValue(timeline.ActionId, out data) && data != null)
            {
                return true;
            }

            data = timeline.ToSkillActionData();
            playerSkillById[timeline.ActionId] = data;
            return true;
        }

        private static bool TryGetOrCreatePlayerEvade(PlayerEvadeTimelineAsset timeline, out PlayerEvadeActionData data)
        {
            if (playerEvadeById.TryGetValue(timeline.ActionId, out data) && data != null)
            {
                return true;
            }

            data = timeline.ToEvadeActionData();
            playerEvadeById[timeline.ActionId] = data;
            return true;
        }

        private static bool TryGetOrCreatePlayerGuard(PlayerGuardTimelineAsset timeline, out PlayerGuardActionData data)
        {
            if (playerGuardById.TryGetValue(timeline.ActionId, out data) && data != null)
            {
                return true;
            }

            data = timeline.ToGuardActionData();
            playerGuardById[timeline.ActionId] = data;
            return true;
        }

        private static bool TryGetOrCreatePlayerReaction(PlayerReactionTimelineAsset timeline, out CombatTimelineReactionConfig config)
        {
            if (playerReactionById.TryGetValue(timeline.ActionId, out config))
            {
                return true;
            }

            config = timeline.ToReactionConfig();
            playerReactionById[timeline.ActionId] = config;
            return true;
        }

        private static bool TryGetOrCreateBossAttack(BossAttackTimelineAsset timeline, out BossAttackDefinition definition)
        {
            if (bossAttackById.TryGetValue(timeline.ActionId, out definition) && definition != null)
            {
                return true;
            }

            definition = timeline.ToBossAttackDefinition();
            bossAttackById[timeline.ActionId] = definition;
            return true;
        }

        private static bool TryGetOrCreateBossReaction(BossReactionTimelineAsset timeline, out CombatTimelineReactionConfig config)
        {
            if (bossReactionById.TryGetValue(timeline.ActionId, out config))
            {
                return true;
            }

            config = timeline.ToReactionConfig();
            bossReactionById[timeline.ActionId] = config;
            return true;
        }
    }
}
