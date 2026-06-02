using System;
using NiumaCore.Module;
using NiumaGrowth.Config;
using NiumaGrowth.Data;
using NiumaGrowth.Service;
using UnityEngine;

namespace NiumaGrowth.Controller
{
    /// <summary>
    /// NiumaGrowth 根控制器。负责把纯 C# GrowthService 接入 Unity 生命周期、Inspector 配置和 GameContext。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NiumaGrowthController : MonoBehaviour, IGameModule
    {
        [Header("成长配置")]
        [Tooltip("技艺成长配置列表。SkillId 必须稳定且不可重复。")]
        [SerializeField] private GrowthSkillDefinition[] growthDefinitions = Array.Empty<GrowthSkillDefinition>();

        [Header("模块启动")]
        [Tooltip("Awake 时是否自动初始化成长服务。没有统一模块启动器时建议开启。")]
        [SerializeField] private bool initializeOnAwake = true;

        [Tooltip("OnEnable 时是否自动启动模块。成长模块第一版没有 Tick 逻辑，但保持统一生命周期。")]
        [SerializeField] private bool startOnEnable = true;

        [Tooltip("初始化时是否把 IGrowthService / IGrowthQuery / IGrowthCommand 注册到 GameContext。")]
        [SerializeField] private bool registerServiceToContext = true;

        [Header("调试：成长请求")]
        [Tooltip("调试用 ActorId。右键菜单添加经验、设置经验、重置进度时使用。")]
        [SerializeField] private string debugActorId = "player";

        [Tooltip("调试用技艺 ID。必须与 GrowthSkillDefinition.SkillId 一致。")]
        [SerializeField] private string debugSkillId;

        [Tooltip("调试增加的经验值。小于等于 0 时服务会拒绝请求。")]
        [SerializeField] private int debugAddExpAmount = 10;

        [Tooltip("调试设置的总经验值。小于 0 时服务层会按 0 处理。")]
        [SerializeField] private int debugSetTotalExp;

        [Tooltip("调试来源模块名。用于事件、日志和排查经验来源。")]
        [SerializeField] private string debugSourceModule = "debug";

        [Header("调试：传承请求")]
        [Tooltip("调试传承来源 ActorId。")]
        [SerializeField] private string debugInheritanceSourceActorId = "player";

        [Tooltip("调试传承目标 ActorId。")]
        [SerializeField] private string debugInheritanceTargetActorId = "npc";

        [Tooltip("调试传承经验倍率。1 表示完整继承，0.5 表示继承一半。")]
        [SerializeField] private float debugInheritanceMultiplier = 1f;

        private IGrowthService _growthService;
        private IGrowthConfigurationService _configurationService;
        private GameContext _context;
        private bool _warnedInitializeFailure;
        private bool _warnedServiceNotReady;

        public string ModuleName => "NiumaGrowth";
        public bool IsInitialized { get; private set; }
        public bool IsRunning { get; private set; }
        public long GrowthRevision => _growthService != null ? _growthService.Revision : 0L;
        public GrowthSkillDefinition[] GrowthDefinitions => growthDefinitions ?? Array.Empty<GrowthSkillDefinition>();
        public IGrowthService GrowthService => _growthService;
        public IGrowthQuery GrowthQuery => _growthService;
        public IGrowthCommand GrowthCommand => _growthService;
        public GrowthOperationResult LastOperationResult { get; private set; }

        private void Awake()
        {
            if (initializeOnAwake && !IsInitialized)
            {
                Initialize(null);
            }
        }

        private void OnEnable()
        {
            if (startOnEnable && IsInitialized && !IsRunning)
            {
                StartModule();
            }
        }

        private void OnDisable()
        {
            StopModule();
        }

        private void OnDestroy()
        {
            UnregisterServicesFromContext();
            _growthService = null;
            _configurationService = null;
            IsInitialized = false;
            IsRunning = false;
        }

        public void Initialize(GameContext context)
        {
            var previousService = _growthService;
            var previousConfig = _configurationService;
            var previousContext = _context;
            var wasRunning = IsRunning;
            var wasInitialized = IsInitialized;
            var targetContext = context ?? _context;
            var previousRegisteredService = targetContext != null ? targetContext.GetService<IGrowthService>() : null;
            var previousRegisteredQuery = targetContext != null ? targetContext.GetService<IGrowthQuery>() : null;
            var previousRegisteredCommand = targetContext != null ? targetContext.GetService<IGrowthCommand>() : null;
            var initializedSuccessfully = false;
            GrowthService newService = null;
            IsRunning = false;

            try
            {
                _context = targetContext;
                var snapshot = previousService != null ? previousService.ExportSnapshot() : null;
                newService = new GrowthService(growthDefinitions, _context?.EventBus);
                if (snapshot != null)
                {
                    LastOperationResult = newService.ImportSnapshot(snapshot);
                }

                _growthService = newService;
                _configurationService = newService;
                RegisterServicesToContext();
                IsInitialized = true;
                _warnedInitializeFailure = false;
                _warnedServiceNotReady = false;
                initializedSuccessfully = true;
            }
            catch (Exception ex)
            {
                if (!_warnedInitializeFailure)
                {
                    Debug.LogError($"[NiumaGrowth] 初始化失败：{ex.Message}", this);
                    _warnedInitializeFailure = true;
                }

                RestoreRegisteredGrowthServices(targetContext, previousRegisteredService, previousRegisteredQuery, previousRegisteredCommand, newService);
                _growthService = previousService;
                _configurationService = previousConfig;
                _context = previousContext;
                IsInitialized = wasInitialized;
            }
            finally
            {
                IsRunning = initializedSuccessfully ? wasRunning : wasRunning && previousService != null;
            }
        }

        public void StartModule()
        {
            if (!IsInitialized)
            {
                Initialize(_context);
            }

            IsRunning = _growthService != null;
        }

        public void StopModule()
        {
            IsRunning = false;
        }

        public void Tick(float deltaTime)
        {
            // 成长模块第一版没有逐帧逻辑。Tick 只用于满足统一 IGameModule 生命周期。
        }

        public void SetGrowthDefinitions(GrowthSkillDefinition[] definitions)
        {
            growthDefinitions = definitions ?? Array.Empty<GrowthSkillDefinition>();
            _configurationService?.SetDefinitions(growthDefinitions);
        }

        /// <summary>
        /// 重新注入事件总线。通常由统一启动器或 GameContext 初始化流程调用。
        /// </summary>
        public void SetEventBus(NiumaCore.Event.IEventBus eventBus)
        {
            _configurationService?.SetEventBus(eventBus);
        }

        public GrowthOperationResult AddExp(GrowthExpRequest request)
        {
            if (!EnsureServiceReady())
            {
                LastOperationResult = GrowthOperationResult.Failed(GrowthFailureReason.ServiceNotReady, request?.ActorId, request?.SkillId, "成长服务尚未初始化。");
                return LastOperationResult;
            }

            LastOperationResult = _growthService.AddExp(request);
            return LastOperationResult;
        }

        public GrowthOperationResult SetExp(string actorId, string skillId, int totalExp, string sourceModule = null)
        {
            if (!EnsureServiceReady())
            {
                LastOperationResult = GrowthOperationResult.Failed(GrowthFailureReason.ServiceNotReady, actorId, skillId, "成长服务尚未初始化。");
                return LastOperationResult;
            }

            LastOperationResult = _growthService.SetExp(actorId, skillId, totalExp, sourceModule);
            return LastOperationResult;
        }

        public GrowthOperationResult ResetProgress(string actorId, string skillId, string sourceModule = null)
        {
            if (!EnsureServiceReady())
            {
                LastOperationResult = GrowthOperationResult.Failed(GrowthFailureReason.ServiceNotReady, actorId, skillId, "成长服务尚未初始化。");
                return LastOperationResult;
            }

            LastOperationResult = _growthService.ResetProgress(actorId, skillId, sourceModule);
            return LastOperationResult;
        }

        public GrowthOperationResult ApplyInheritance(GrowthInheritanceRequest request)
        {
            if (!EnsureServiceReady())
            {
                LastOperationResult = GrowthOperationResult.Failed(GrowthFailureReason.ServiceNotReady, request?.TargetActorId, null, "成长服务尚未初始化。");
                return LastOperationResult;
            }

            LastOperationResult = _growthService.ApplyInheritance(request);
            return LastOperationResult;
        }

        public int GetLevel(string actorId, string skillId)
        {
            return EnsureServiceReady(false) ? _growthService.GetLevel(actorId, skillId) : 0;
        }

        public int GetTotalExp(string actorId, string skillId)
        {
            return EnsureServiceReady(false) ? _growthService.GetTotalExp(actorId, skillId) : 0;
        }

        public GrowthProgressViewData GetProgress(string actorId, string skillId)
        {
            return EnsureServiceReady(false) ? _growthService.GetProgress(actorId, skillId) : null;
        }

        public bool MeetsRequirement(string actorId, GrowthRequirementData requirement)
        {
            return EnsureServiceReady(false) && _growthService.MeetsRequirement(actorId, requirement);
        }

        public GrowthProgressViewData[] GetAllProgress(string actorId, bool includeNotStarted = true)
        {
            return EnsureServiceReady(false) ? _growthService.GetAllProgress(actorId, includeNotStarted) : Array.Empty<GrowthProgressViewData>();
        }

        public GrowthSaveData ExportSnapshot()
        {
            return EnsureServiceReady(false) ? _growthService.ExportSnapshot() : new GrowthSaveData();
        }

        public GrowthOperationResult ImportSnapshot(GrowthSaveData snapshot)
        {
            if (!EnsureServiceReady())
            {
                LastOperationResult = GrowthOperationResult.Failed(GrowthFailureReason.ServiceNotReady, null, null, "成长服务尚未初始化。");
                return LastOperationResult;
            }

            LastOperationResult = _growthService.ImportSnapshot(snapshot);
            return LastOperationResult;
        }

        [ContextMenu("NiumaGrowth/调试/重新初始化模块")]
        private void DebugReinitialize()
        {
            Initialize(_context);
        }

        [ContextMenu("NiumaGrowth/调试/启动模块")]
        private void DebugStartModule()
        {
            StartModule();
        }

        [ContextMenu("NiumaGrowth/调试/停止模块")]
        private void DebugStopModule()
        {
            StopModule();
        }

        [ContextMenu("NiumaGrowth/调试/增加经验")]
        private void DebugAddExp()
        {
            var result = AddExp(new GrowthExpRequest
            {
                ActorId = debugActorId,
                SkillId = debugSkillId,
                Amount = debugAddExpAmount,
                SourceModule = debugSourceModule,
                Reason = "InspectorContextMenu"
            });
            Debug.Log($"[NiumaGrowth] 调试增加经验结果：Succeeded={result?.Succeeded}, Reason={result?.FailureReason}, Message={result?.Message}", this);
        }

        [ContextMenu("NiumaGrowth/调试/设置总经验")]
        private void DebugSetExp()
        {
            var result = SetExp(debugActorId, debugSkillId, debugSetTotalExp, debugSourceModule);
            Debug.Log($"[NiumaGrowth] 调试设置经验结果：Succeeded={result?.Succeeded}, Reason={result?.FailureReason}, Message={result?.Message}", this);
        }

        [ContextMenu("NiumaGrowth/调试/重置技艺进度")]
        private void DebugResetProgress()
        {
            var result = ResetProgress(debugActorId, debugSkillId, debugSourceModule);
            Debug.Log($"[NiumaGrowth] 调试重置结果：Succeeded={result?.Succeeded}, Reason={result?.FailureReason}, Message={result?.Message}", this);
        }

        [ContextMenu("NiumaGrowth/调试/应用传承")]
        private void DebugApplyInheritance()
        {
            var result = ApplyInheritance(new GrowthInheritanceRequest
            {
                SourceActorId = debugInheritanceSourceActorId,
                TargetActorId = debugInheritanceTargetActorId,
                ExpMultiplier = debugInheritanceMultiplier,
                SourceModule = debugSourceModule
            });
            Debug.Log($"[NiumaGrowth] 调试传承结果：Succeeded={result?.Succeeded}, Reason={result?.FailureReason}, Message={result?.Message}", this);
        }

        [ContextMenu("NiumaGrowth/调试/打印当前技艺")]
        private void DebugPrintProgress()
        {
            var progress = GetProgress(debugActorId, debugSkillId);
            if (progress == null)
            {
                Debug.Log($"[NiumaGrowth] 未找到技艺进度：ActorId={debugActorId}, SkillId={debugSkillId}", this);
                return;
            }

            Debug.Log($"[NiumaGrowth] Actor={debugActorId}, Skill={debugSkillId}, Level={progress.Level}, TotalExp={progress.TotalExp}, Next={progress.NextLevelExp}, Missing={progress.IsMissingDefinition}", this);
        }

        private bool EnsureServiceReady(bool allowInitialize = true)
        {
            if (_growthService != null)
            {
                return true;
            }

            if (allowInitialize)
            {
                Initialize(_context);
            }

            var ready = _growthService != null;
            if (!ready && !_warnedServiceNotReady)
            {
                Debug.LogWarning("[NiumaGrowth] 成长服务尚未初始化。", this);
                _warnedServiceNotReady = true;
            }

            return ready;
        }

        private void RegisterServicesToContext()
        {
            if (!registerServiceToContext || _context == null || _growthService == null)
            {
                return;
            }

            _context.RegisterService<IGrowthService>(_growthService);
            _context.RegisterService<IGrowthQuery>(_growthService);
            _context.RegisterService<IGrowthCommand>(_growthService);
        }

        private void RestoreRegisteredGrowthServices(GameContext targetContext, IGrowthService previousService, IGrowthQuery previousQuery, IGrowthCommand previousCommand, IGrowthService failedService)
        {
            if (targetContext == null)
            {
                return;
            }

            if (ReferenceEquals(targetContext.GetService<IGrowthService>(), failedService))
            {
                RestoreService(targetContext, previousService);
            }

            if (ReferenceEquals(targetContext.GetService<IGrowthQuery>(), failedService))
            {
                RestoreService(targetContext, previousQuery);
            }

            if (ReferenceEquals(targetContext.GetService<IGrowthCommand>(), failedService))
            {
                RestoreService(targetContext, previousCommand);
            }
        }

        private static void RestoreService<T>(GameContext context, T previousService) where T : class
        {
            if (previousService != null)
            {
                context.RegisterService(previousService);
            }
            else
            {
                context.UnregisterService<T>();
            }
        }

        private void UnregisterServicesFromContext()
        {
            if (_context == null)
            {
                return;
            }

            if (ReferenceEquals(_context.GetService<IGrowthService>(), _growthService)) _context.UnregisterService<IGrowthService>();
            if (ReferenceEquals(_context.GetService<IGrowthQuery>(), _growthService)) _context.UnregisterService<IGrowthQuery>();
            if (ReferenceEquals(_context.GetService<IGrowthCommand>(), _growthService)) _context.UnregisterService<IGrowthCommand>();
        }
    }
}
