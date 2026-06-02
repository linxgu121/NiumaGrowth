using System;
using System.Collections.Generic;
using NiumaCore.Event;
using NiumaGrowth.Config;
using NiumaGrowth.Data;
using UnityEngine;

namespace NiumaGrowth.Service
{
    /// <summary>
    /// 技艺成长核心服务。只保存经验事实，等级始终由配置阈值实时推导。
    /// </summary>
    public sealed class GrowthService : IGrowthService, IGrowthConfigurationService
    {
        private readonly Dictionary<string, GrowthSkillDefinition> _definitions = new Dictionary<string, GrowthSkillDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, GrowthProgressSnapshot>> _progressByActor = new Dictionary<string, Dictionary<string, GrowthProgressSnapshot>>(StringComparer.Ordinal);
        private readonly List<GrowthProgressViewData> _viewBuffer = new List<GrowthProgressViewData>();
        private IEventBus _eventBus;
        private long _revision;

        public long Revision => _revision;

        public GrowthService(GrowthSkillDefinition[] definitions = null, IEventBus eventBus = null)
        {
            _eventBus = eventBus;
            SetDefinitions(definitions);
        }

        /// <summary>
        /// 注入事件总线。没有事件总线时 Growth 仍然正常工作，只是不发布生命周期事件。
        /// </summary>
        public void SetEventBus(IEventBus eventBus)
        {
            _eventBus = eventBus;
        }

        public void SetDefinitions(GrowthSkillDefinition[] definitions)
        {
            _definitions.Clear();
            if (definitions == null)
            {
                return;
            }

            for (var i = 0; i < definitions.Length; i++)
            {
                var definition = definitions[i];
                if (definition == null || string.IsNullOrWhiteSpace(definition.SkillId))
                {
                    continue;
                }

                if (!ValidateDefinition(definition))
                {
                    continue;
                }

                if (!_definitions.ContainsKey(definition.SkillId))
                {
                    _definitions.Add(definition.SkillId, definition);
                }
                else
                {
                    Debug.LogWarning($"[NiumaGrowth] 检测到重复 SkillId={definition.SkillId}，后出现的配置已被忽略。", definition);
                }
            }

            RefreshMissingFlags();
        }

        public int GetLevel(string actorId, string skillId)
        {
            return GetProgress(actorId, skillId)?.Level ?? 0;
        }

        public int GetTotalExp(string actorId, string skillId)
        {
            return TryGetProgress(actorId, skillId, out var snapshot) ? Math.Max(0, snapshot.TotalExp) : 0;
        }

        public GrowthProgressViewData GetProgress(string actorId, string skillId)
        {
            if (string.IsNullOrWhiteSpace(skillId))
            {
                return null;
            }

            TryGetProgress(actorId, skillId, out var snapshot);
            if (snapshot == null && !_definitions.ContainsKey(skillId))
            {
                return null;
            }

            return BuildViewData(skillId, snapshot);
        }

        public GrowthProgressViewData[] GetAllProgress(string actorId, bool includeNotStarted = true)
        {
            _viewBuffer.Clear();
            var included = new HashSet<string>(StringComparer.Ordinal);
            if (_progressByActor.TryGetValue(actorId ?? string.Empty, out var map))
            {
                foreach (var pair in map)
                {
                    _viewBuffer.Add(BuildViewData(pair.Key, pair.Value));
                    included.Add(pair.Key);
                }
            }

            if (includeNotStarted)
            {
                foreach (var pair in _definitions)
                {
                    if (included.Contains(pair.Key))
                    {
                        continue;
                    }

                    _viewBuffer.Add(BuildViewData(pair.Key, null));
                }
            }

            _viewBuffer.Sort(CompareViewData);
            var result = new GrowthProgressViewData[_viewBuffer.Count];
            for (var i = 0; i < _viewBuffer.Count; i++)
            {
                result[i] = _viewBuffer[i]?.Clone();
            }

            return result;
        }

        public bool MeetsRequirement(string actorId, GrowthRequirementData requirement)
        {
            if (requirement == null || string.IsNullOrWhiteSpace(requirement.SkillId))
            {
                return true;
            }

            return GetLevel(actorId, requirement.SkillId) >= Math.Max(0, requirement.RequiredLevel);
        }

        public GrowthOperationResult AddExp(GrowthExpRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ActorId) || string.IsNullOrWhiteSpace(request.SkillId) || request.Amount <= 0)
            {
                return GrowthOperationResult.Failed(GrowthFailureReason.InvalidRequest, request?.ActorId, request?.SkillId, "AddExp 请求无效。");
            }

            var snapshot = GetOrCreateProgress(request.ActorId, request.SkillId);
            var oldExp = snapshot.TotalExp;
            var oldLevel = CalculateLevelForSkill(request.SkillId, oldExp);
            snapshot.TotalExp = SafeAdd(snapshot.TotalExp, request.Amount);
            snapshot.IsMissingDefinition = !_definitions.ContainsKey(request.SkillId);
            if (snapshot.TotalExp == oldExp)
            {
                return GrowthOperationResult.Success(request.ActorId, request.SkillId, BuildViewData(request.SkillId, snapshot), "经验未变化。");
            }

            BumpRevision();
            PublishProgressChanged(request.ActorId, request.SkillId, oldExp, snapshot.TotalExp, oldLevel, CalculateLevelForSkill(request.SkillId, snapshot.TotalExp), request.SourceModule);
            return GrowthOperationResult.Success(request.ActorId, request.SkillId, BuildViewData(request.SkillId, snapshot));
        }

        public GrowthOperationResult SetExp(string actorId, string skillId, int totalExp, string sourceModule = null)
        {
            if (string.IsNullOrWhiteSpace(actorId) || string.IsNullOrWhiteSpace(skillId))
            {
                return GrowthOperationResult.Failed(GrowthFailureReason.InvalidRequest, actorId, skillId, "ActorId 或 SkillId 为空。");
            }

            var snapshot = GetOrCreateProgress(actorId, skillId);
            var clamped = Math.Max(0, totalExp);
            var oldExp = snapshot.TotalExp;
            var oldLevel = CalculateLevelForSkill(skillId, oldExp);
            if (snapshot.TotalExp == clamped)
            {
                return GrowthOperationResult.Success(actorId, skillId, BuildViewData(skillId, snapshot), "经验未变化。");
            }

            snapshot.TotalExp = clamped;
            snapshot.IsMissingDefinition = !_definitions.ContainsKey(skillId);
            BumpRevision();
            PublishProgressChanged(actorId, skillId, oldExp, snapshot.TotalExp, oldLevel, CalculateLevelForSkill(skillId, snapshot.TotalExp), sourceModule);
            return GrowthOperationResult.Success(actorId, skillId, BuildViewData(skillId, snapshot));
        }

        public GrowthOperationResult ResetProgress(string actorId, string skillId, string sourceModule = null)
        {
            if (string.IsNullOrWhiteSpace(actorId) || string.IsNullOrWhiteSpace(skillId))
            {
                return GrowthOperationResult.Failed(GrowthFailureReason.InvalidRequest, actorId, skillId, "ActorId 或 SkillId 为空。");
            }

            if (!_progressByActor.TryGetValue(actorId, out var map) || !map.Remove(skillId))
            {
                return GrowthOperationResult.Success(actorId, skillId, null, "没有可重置的成长进度。");
            }

            if (map.Count == 0)
            {
                _progressByActor.Remove(actorId);
            }

            BumpRevision();
            PublishDeferred(new GrowthProgressResetEvent(actorId, skillId, sourceModule));
            return GrowthOperationResult.Success(actorId, skillId, null);
        }

        public GrowthOperationResult ApplyInheritance(GrowthInheritanceRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.SourceActorId) || string.IsNullOrWhiteSpace(request.TargetActorId))
            {
                return GrowthOperationResult.Failed(GrowthFailureReason.InvalidRequest, request?.TargetActorId, null, "传承请求无效。");
            }

            if (!_progressByActor.TryGetValue(request.SourceActorId, out var source) || source.Count == 0)
            {
                return GrowthOperationResult.Success(request.TargetActorId, null, null, "来源 Actor 没有成长进度。");
            }

            var multiplier = Math.Max(0f, request.ExpMultiplier);
            var target = GetOrCreateActorMap(request.TargetActorId);
            var changed = false;
            foreach (var pair in source)
            {
                var definition = FindDefinition(pair.Key);
                if (definition != null && !definition.IsInheritable)
                {
                    continue;
                }

                var inheritedExp = Math.Max(0, (int)Math.Round(pair.Value.TotalExp * multiplier));
                if (inheritedExp <= 0)
                {
                    continue;
                }

                if (!target.TryGetValue(pair.Key, out var targetProgress))
                {
                    targetProgress = new GrowthProgressSnapshot { SkillId = pair.Key };
                    target.Add(pair.Key, targetProgress);
                }

                if (targetProgress.TotalExp < inheritedExp)
                {
                    var oldExp = targetProgress.TotalExp;
                    var oldLevel = CalculateLevelForSkill(pair.Key, oldExp);
                    targetProgress.TotalExp = inheritedExp;
                    targetProgress.IsMissingDefinition = definition == null;
                    PublishProgressChanged(request.TargetActorId, pair.Key, oldExp, targetProgress.TotalExp, oldLevel, CalculateLevelForSkill(pair.Key, targetProgress.TotalExp), request.SourceModule);
                    changed = true;
                }
            }

            if (changed)
            {
                BumpRevision();
                PublishDeferred(new GrowthInheritanceAppliedEvent(request.SourceActorId, request.TargetActorId, multiplier, request.SourceModule));
            }

            return GrowthOperationResult.Success(request.TargetActorId, null, null, changed ? null : "传承未产生经验变化。");
        }

        public GrowthSaveData ExportSnapshot()
        {
            var owners = new List<GrowthOwnerSnapshot>(_progressByActor.Count);
            foreach (var actorPair in _progressByActor)
            {
                var skills = new List<GrowthProgressSnapshot>();
                foreach (var skillPair in actorPair.Value)
                {
                    if (skillPair.Value == null || skillPair.Value.TotalExp <= 0)
                    {
                        continue;
                    }

                    skills.Add(skillPair.Value.Clone());
                }

                if (skills.Count > 0)
                {
                    owners.Add(new GrowthOwnerSnapshot
                    {
                        ActorId = actorPair.Key,
                        Skills = skills.ToArray()
                    });
                }
            }

            return new GrowthSaveData
            {
                Version = 1,
                Revision = _revision,
                Owners = owners.ToArray()
            };
        }

        public GrowthOperationResult ImportSnapshot(GrowthSaveData snapshot)
        {
            if (snapshot == null || snapshot.Version != 1 || snapshot.Owners == null || snapshot.Revision < 0L)
            {
                return GrowthOperationResult.Failed(GrowthFailureReason.ImportInvalid, null, null, "成长存档数据无效。");
            }

            var imported = new Dictionary<string, Dictionary<string, GrowthProgressSnapshot>>(StringComparer.Ordinal);
            for (var i = 0; i < snapshot.Owners.Length; i++)
            {
                var owner = snapshot.Owners[i];
                if (owner == null || string.IsNullOrWhiteSpace(owner.ActorId) || owner.Skills == null)
                {
                    return GrowthOperationResult.Failed(GrowthFailureReason.ImportInvalid, null, null, $"成长存档 Owners[{i}] 数据无效。");
                }

                if (imported.ContainsKey(owner.ActorId))
                {
                    return GrowthOperationResult.Failed(GrowthFailureReason.ImportInvalid, owner.ActorId, null, $"成长存档中存在重复 ActorId：{owner.ActorId}。");
                }

                var map = new Dictionary<string, GrowthProgressSnapshot>(StringComparer.Ordinal);
                for (var j = 0; j < owner.Skills.Length; j++)
                {
                    var progress = owner.Skills[j];
                    if (progress == null || string.IsNullOrWhiteSpace(progress.SkillId))
                    {
                        return GrowthOperationResult.Failed(GrowthFailureReason.ImportInvalid, owner.ActorId, null, $"成长存档 Actor={owner.ActorId} 的 Skills[{j}] 数据无效。");
                    }

                    if (map.ContainsKey(progress.SkillId))
                    {
                        return GrowthOperationResult.Failed(GrowthFailureReason.ImportInvalid, owner.ActorId, progress.SkillId, $"成长存档中 Actor={owner.ActorId} 存在重复 SkillId：{progress.SkillId}。");
                    }

                    map[progress.SkillId] = new GrowthProgressSnapshot
                    {
                        SkillId = progress.SkillId,
                        TotalExp = Math.Max(0, progress.TotalExp),
                        IsMissingDefinition = !_definitions.ContainsKey(progress.SkillId)
                    };
                }

                if (map.Count > 0)
                {
                    imported[owner.ActorId] = map;
                }
            }

            _progressByActor.Clear();
            foreach (var pair in imported)
            {
                _progressByActor[pair.Key] = pair.Value;
            }

            _revision = Math.Max(0L, snapshot.Revision);
            return GrowthOperationResult.Success(null, null, null, "成长快照导入完成。");
        }

        private GrowthProgressSnapshot GetOrCreateProgress(string actorId, string skillId)
        {
            var map = GetOrCreateActorMap(actorId);
            if (!map.TryGetValue(skillId, out var snapshot))
            {
                snapshot = new GrowthProgressSnapshot
                {
                    SkillId = skillId,
                    TotalExp = 0,
                    IsMissingDefinition = !_definitions.ContainsKey(skillId)
                };
                map.Add(skillId, snapshot);
            }

            return snapshot;
        }

        private Dictionary<string, GrowthProgressSnapshot> GetOrCreateActorMap(string actorId)
        {
            if (!_progressByActor.TryGetValue(actorId, out var map))
            {
                map = new Dictionary<string, GrowthProgressSnapshot>(StringComparer.Ordinal);
                _progressByActor.Add(actorId, map);
            }

            return map;
        }

        private bool TryGetProgress(string actorId, string skillId, out GrowthProgressSnapshot snapshot)
        {
            snapshot = null;
            return !string.IsNullOrWhiteSpace(actorId)
                   && !string.IsNullOrWhiteSpace(skillId)
                   && _progressByActor.TryGetValue(actorId, out var map)
                   && map.TryGetValue(skillId, out snapshot);
        }

        private GrowthProgressViewData BuildViewData(string skillId, GrowthProgressSnapshot snapshot)
        {
            var totalExp = Math.Max(0, snapshot?.TotalExp ?? 0);
            var definition = FindDefinition(skillId);
            var missing = definition == null;
            var thresholds = definition != null ? definition.LevelThresholds : null;
            var level = CalculateLevel(totalExp, thresholds);
            var maxLevel = thresholds != null ? thresholds.Length : 0;
            var isMax = maxLevel > 0 && level >= maxLevel;
            var currentLevelExp = level <= 0 || thresholds == null ? 0 : thresholds[Math.Min(level - 1, thresholds.Length - 1)];
            var nextLevelExp = !isMax && thresholds != null && level < thresholds.Length ? thresholds[level] : -1;
            var expInLevel = Math.Max(0, totalExp - currentLevelExp);
            var expToNext = nextLevelExp > currentLevelExp ? nextLevelExp - currentLevelExp : 0;
            return new GrowthProgressViewData
            {
                SkillId = skillId,
                DisplayName = missing ? $"未知技艺({skillId})" : (!string.IsNullOrWhiteSpace(definition.DisplayName) ? definition.DisplayName : definition.SkillId),
                Description = missing ? string.Empty : definition.Description,
                IconAddress = missing ? string.Empty : definition.IconAddress,
                Category = missing ? GrowthCategory.None : definition.Category,
                Level = level,
                TotalExp = totalExp,
                CurrentLevelExp = currentLevelExp,
                NextLevelExp = nextLevelExp,
                ExpInCurrentLevel = expInLevel,
                ExpToNextLevel = expToNext,
                Progress01 = expToNext > 0 ? Math.Max(0f, Math.Min(1f, expInLevel / (float)expToNext)) : 1f,
                IsMaxLevel = isMax,
                IsMissingDefinition = missing || (snapshot != null && snapshot.IsMissingDefinition),
                IsInheritable = definition == null || definition.IsInheritable
            };
        }

        private GrowthSkillDefinition FindDefinition(string skillId)
        {
            return !string.IsNullOrWhiteSpace(skillId) && _definitions.TryGetValue(skillId, out var definition) ? definition : null;
        }

        private int CalculateLevelForSkill(string skillId, int totalExp)
        {
            var definition = FindDefinition(skillId);
            return CalculateLevel(Math.Max(0, totalExp), definition != null ? definition.LevelThresholds : null);
        }

        private static int CalculateLevel(int totalExp, int[] thresholds)
        {
            if (thresholds == null || thresholds.Length == 0)
            {
                return 0;
            }

            var level = 0;
            for (var i = 0; i < thresholds.Length; i++)
            {
                if (totalExp >= thresholds[i])
                {
                    level = i + 1;
                }
            }

            return level;
        }

        private void RefreshMissingFlags()
        {
            foreach (var actorPair in _progressByActor)
            {
                foreach (var skillPair in actorPair.Value)
                {
                    if (skillPair.Value != null)
                    {
                        skillPair.Value.IsMissingDefinition = !_definitions.ContainsKey(skillPair.Key);
                    }
                }
            }
        }

        private static bool ValidateDefinition(GrowthSkillDefinition definition)
        {
            if (definition.LevelThresholds == null || definition.LevelThresholds.Length == 0)
            {
                return true;
            }

            var previous = -1;
            for (var i = 0; i < definition.LevelThresholds.Length; i++)
            {
                var threshold = definition.LevelThresholds[i];
                if (threshold < 0)
                {
                    Debug.LogError($"[NiumaGrowth] GrowthSkillDefinition={definition.name} 的 LevelThresholds[{i}] 为负数，该配置不会进入可用表。", definition);
                    return false;
                }

                if (threshold <= previous)
                {
                    Debug.LogError($"[NiumaGrowth] GrowthSkillDefinition={definition.name} 的 LevelThresholds 必须严格递增，该配置不会进入可用表。", definition);
                    return false;
                }

                previous = threshold;
            }

            return true;
        }

        private void BumpRevision()
        {
            _revision = _revision == long.MaxValue ? long.MaxValue : _revision + 1L;
        }

        private void PublishProgressChanged(string actorId, string skillId, int oldExp, int newExp, int oldLevel, int newLevel, string sourceModule)
        {
            PublishDeferred(new GrowthExpChangedEvent(actorId, skillId, oldExp, newExp, oldLevel, newLevel, sourceModule));
            if (oldLevel != newLevel)
            {
                PublishDeferred(new GrowthLevelChangedEvent(actorId, skillId, oldLevel, newLevel, sourceModule));
            }
        }

        private void PublishDeferred<T>(T evt)
        {
            _eventBus?.Publish(evt, EventChannel.Deferred);
        }

        private static int SafeAdd(int left, int right)
        {
            var result = (long)Math.Max(0, left) + Math.Max(0, right);
            return result > int.MaxValue ? int.MaxValue : (int)result;
        }

        private static int CompareViewData(GrowthProgressViewData left, GrowthProgressViewData right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return 1;
            if (right == null) return -1;

            var category = left.Category.CompareTo(right.Category);
            return category != 0 ? category : string.CompareOrdinal(left.SkillId, right.SkillId);
        }
    }
}
