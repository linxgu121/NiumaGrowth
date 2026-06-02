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

        private IGrowthService _growthService;
        private IGrowthConfigurationService _configurationService;
        private GameContext _context;

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

            try
            {
                _context = context ?? _context;
                var snapshot = previousService != null ? previousService.ExportSnapshot() : null;
                var newService = new GrowthService(growthDefinitions);
                if (snapshot != null)
                {
                    LastOperationResult = newService.ImportSnapshot(snapshot);
                }

                _growthService = newService;
                _configurationService = newService;
                RegisterServicesToContext();
                IsInitialized = true;
                IsRunning = wasRunning;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NiumaGrowth] 初始化失败：{ex.Message}", this);
                _growthService = previousService;
                _configurationService = previousConfig;
                _context = previousContext;
                IsInitialized = wasInitialized;
                IsRunning = wasRunning && previousService != null;
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
        }

        public void SetGrowthDefinitions(GrowthSkillDefinition[] definitions)
        {
            growthDefinitions = definitions ?? Array.Empty<GrowthSkillDefinition>();
            _configurationService?.SetDefinitions(growthDefinitions);
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

        public int GetLevel(string actorId, string skillId)
        {
            return EnsureServiceReady(false) ? _growthService.GetLevel(actorId, skillId) : 0;
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

            return _growthService != null;
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
