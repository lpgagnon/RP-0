﻿using KSPAPIExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

using static RP0.ProceduralAvionics.ProceduralAvionicsUtils;

namespace RP0.ProceduralAvionics
{
	class ModuleProceduralAvionics : ModuleAvionics, IPartMassModifier, IPartCostModifier
	{

		#region KSPFields, overrides, and class variables

		const string kwFormat = "{0:0.##}";
		const string wFormat = "{0:0}";
        const float FLOAT_TOLERANCE = 1.00001f;
        private const float InternalTanksVolumeUtilization = 7f / 9;

        [KSPField(isPersistant = true, guiName = "Contr. Mass", guiActive = false, guiActiveEditor = true, guiUnits = "\u2009t"),
		 UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0f, incrementLarge = 10f, incrementSmall = 1f, incrementSlide = 0.05f, sigFigs = 3, unit = "\u2009t")]
		public float controllableMass = -1;

		[KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Configuration"), UI_ChooseOption(scene = UI_Scene.Editor)]
		public string avionicsConfigName;
		private string oldAvionicsConfigName;

		[KSPField(isPersistant = true)]
		public string avionicsTechLevel;
        private string oldAvionicsTechLevel;

        [KSPField(isPersistant = true)]
        public float avionicsDensity;

        [KSPField(isPersistant = true)]
        public float massExponent;

        [KSPField(isPersistant = true)]
        public float massConstant;

        [KSPField(isPersistant = true)]
        public float massFactor;

        [KSPField(isPersistant = true)]
        public float shieldingMassFactor;

        [KSPField(isPersistant = true)]
        public float costExponent;

        [KSPField(isPersistant = true)]
        public float costConstant;

        [KSPField(isPersistant = true)]
        public float costFactor;

        [KSPField(isPersistant = true)]
        public float powerExponent;

        [KSPField(isPersistant = true)]
        public float powerConstant;

        [KSPField(isPersistant = true)]
        public float powerFactor;

        [KSPField(isPersistant = true)]
        public float disabledPowerFactor;

        [KSPField(isPersistant = true)]
		public int SASServiceLevel = -1;

		[KSPField(isPersistant = true)]
		public bool hasScienceContainer = false;

		[KSPField(isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "Avionics Utilization")]
		public string utilizationDisplay;

		[KSPField(isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "Power Requirements")]
		public string powerRequirementsDisplay;

		[KSPField(isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "Avionics Mass")]
		public string massDisplay;

		[KSPField(isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "Avionics Cost")]
		public string costDisplay;

		public ProceduralAvionicsConfig CurrentProceduralAvionicsConfig {
			get { return currentProceduralAvionicsConfig; }
		}

        public ProceduralAvionicsTechNode CurrentProceduralAvionicsTechNode {
			get {
				if (CurrentProceduralAvionicsConfig != null && avionicsTechLevel != null && CurrentProceduralAvionicsConfig.TechNodes.ContainsKey(avionicsTechLevel))
				{
					return CurrentProceduralAvionicsConfig.TechNodes[avionicsTechLevel];
				}
                return new ProceduralAvionicsTechNode();
			}
		}

		protected override float GetInternalMassLimit()
		{
            var oldLimit = controllableMass;
            ClampControllableMass();
            if(controllableMass != oldLimit)
            {
                Log("WARNING: LIMIT WAS RESET IN GET");
            }
            return controllableMass;
		}

        private void ClampControllableMass()
        {
            var max = GetMaximumControllableMass();
            if (max == 0)
            {
                Log($"WARNING: NO MAX volume: {cachedVolume} MaxAvMass: {MaxAvionicsMass} max controllable mass: {GetControllableMass(MaxAvionicsMass)}");
            }

            if (controllableMass > max * FLOAT_TOLERANCE)
            {
                Log("Resetting procedural mass limit to max of ", max, ", was ", controllableMass);
                controllableMass = max;
                RefreshPartWindow();
            }
        }

        private float GetMaximumControllableMass() => FloorToSliderIncrement(GetControllableMass(MaxAvionicsMass));

        private float GetControllableMass(float avionicsMass) => GetInversePolynomial(avionicsMass * 1000, massExponent, massConstant, massFactor);

        private float GetShieldedAvionicsMass()
        {
            var avionicsMass = GetAvionicsMass();
            return avionicsMass + GetShieldingMass(avionicsMass);
        }

        private float GetAvionicsMass() => GetPolynomial(GetInternalMassLimit(), massExponent, massConstant, massFactor) / 1000f;

        private float GetShieldingMass(float avionicsMass) => Mathf.Pow(avionicsMass, 2f / 3) * shieldingMassFactor;

        private float GetAvionicsCost() => GetPolynomial(GetInternalMassLimit(), costExponent, costConstant, costFactor);

        protected override float GetEnabledkW() => GetPolynomial(GetInternalMassLimit(), powerExponent, powerConstant, powerFactor) / 1000f;

        private static float GetPolynomial(float value, float exponent, float constant, float factor) => (Mathf.Pow(value, exponent) + constant) * factor;

        private static float GetInversePolynomial(float value, float exponent, float constant, float factor) => Mathf.Pow(value / factor - constant, 1 / exponent);

        protected override float GetDisabledkW() => GetEnabledkW() * disabledPowerFactor;

        protected override bool GetToggleable() => disabledPowerFactor > 0;

        protected override string GetTonnageString() => "This part can be configured to allow control of vessels up to any mass.";

        private ProceduralAvionicsConfig currentProceduralAvionicsConfig;
		private UI_FloatEdit controllableMassEdit;

		#endregion

		#region event handlers
		public override void OnLoad(ConfigNode node)
		{
			try {
				Log("OnLoad called");
				if (GameSceneFilter.Loading.IsLoaded()) {
					Log("Loading Avionics Configs");
					ProceduralAvionicsTechManager.LoadAvionicsConfigs(node);
				}
			}
			catch (Exception ex) {
				Log("OnLoad exception: ", ex.Message);
				throw;
			}
		}

		private bool started = false;
		public new void Start()
        {
            SetFallbackConfigForLegacyCraft();
            UpdateConfigSliders();
            BindUIChangeCallbacks();
            SetControllableMassForLegacyCraft();
            AvionicsConfigChanged();

            if (cachedEventData != null)
            {
                OnPartVolumeChanged(cachedEventData);
            }

            base.Start();
            started = true;
            Log("Start finished");
        }

        private void SetFallbackConfigForLegacyCraft()
        {
            if (HighLogic.LoadedSceneIsEditor && !ProceduralAvionicsTechManager.GetAvailableConfigs().Contains(avionicsConfigName))
            {
                Log($"No valid config set ({avionicsConfigName})");
                avionicsConfigName = ProceduralAvionicsTechManager.GetPurchasedConfigs().First();
            }
            if (string.IsNullOrEmpty(avionicsTechLevel))
            {
                avionicsTechLevel = ProceduralAvionicsTechManager.GetMaxUnlockedTech(avionicsConfigName);
                Log("No tech level set, using ", avionicsTechLevel);
            }
        }

        private void SetControllableMassForLegacyCraft()
        {
            if (controllableMass < 0)
            {
                controllableMass = HighLogic.LoadedSceneIsFlight ? float.MaxValue : 0;
            }
        }

        private bool callbacksBound = false;
		private void BindUIChangeCallbacks()
		{
			if (!callbacksBound) {
				Fields[nameof(controllableMass)].uiControlEditor.onFieldChanged += ControllableMassChanged;
				Fields[nameof(avionicsConfigName)].uiControlEditor.onFieldChanged += AvionicsConfigChanged;
				callbacksBound = true;
			}
		}

		private void ControllableMassChanged(BaseField arg1, object arg2)
		{
			Log("Mass limit changed");
            ClampControllableMass();
            SetMinVolume();
            SendRemainingVolume();
            RefreshDisplays();
		}

		private void AvionicsConfigChanged(BaseField arg1, object arg2)
		{
			avionicsTechLevel = ProceduralAvionicsTechManager.GetMaxUnlockedTech(avionicsConfigName);

			AvionicsConfigChanged();
		}

		private void AvionicsConfigChanged()
		{
            if (avionicsConfigName == oldAvionicsConfigName && avionicsTechLevel == oldAvionicsTechLevel)
            {
                return;
            }
            Log("Setting config to ", avionicsConfigName);
            currentProceduralAvionicsConfig = ProceduralAvionicsTechManager.GetProceduralAvionicsConfig(avionicsConfigName);
            Log("Setting tech node to ", avionicsTechLevel);
            oldAvionicsConfigName = avionicsConfigName;
            oldAvionicsTechLevel = avionicsTechLevel;
            SetInternalKSPFields();
            ClampControllableMass();
            SetMinVolume(true);
            UpdateControllableMassSlider();
            SendRemainingVolume();
            OnConfigurationUpdated();
            RefreshDisplays();
		}


		private float cachedMinVolume = float.MaxValue;
		public void SetMinVolume(bool forceUpdate = false)
		{
			Log("Setting min volume for proceduralMassLimit of ", controllableMass);
			float minVolume = GetAvionicsMass() / avionicsDensity * FLOAT_TOLERANCE;
			if (float.IsNaN(minVolume)) {
				return;
			}
			Log("min volume should be ", minVolume);
			cachedMinVolume = minVolume;

			PartModule ppModule = null;
			Type ppModuleType = null;
			foreach (var module in part.Modules) {
				var moduleType = module.GetType();
				if (moduleType.FullName == "ProceduralParts.ProceduralPart") {
					ppModule = module;
					ppModuleType = moduleType;
					ppModuleType.GetField("volumeMin").SetValue(ppModule, minVolume);
					Log("Applied min volume");
				}
			}
			Log("minVolume: ", minVolume);
			Log("Comparing against cached volume of ", cachedVolume);
			if (forceUpdate || minVolume > cachedVolume) {
				if (!forceUpdate) {
					Log("cachedVolume too low: ", cachedVolume);
				}
				if (ppModule != null) {
					var reflectedShape = ppModuleType.GetProperty("CurrentShape").GetValue(ppModule, null);
					reflectedShape.GetType().GetMethod("ForceNextUpdate").Invoke(reflectedShape, new object[] { });
					Log("Volume fixed, refreshing part window");
				}
				RefreshPartWindow();
			}
		}

        public void FixedUpdate()
		{
			if (!HighLogic.LoadedSceneIsEditor) {
				SetSASServiceLevel();
				SetScienceContainer();
			}
		}

        public float GetModuleMass(float defaultMass, ModifierStagingSituation sit) => GetMassSafely();
        public ModifierChangeWhen GetModuleMassChangeWhen() => ModifierChangeWhen.FIXED;
        public float GetModuleCost(float defaultCost, ModifierStagingSituation sit) => GetCostSafely();
        public ModifierChangeWhen GetModuleCostChangeWhen() => ModifierChangeWhen.FIXED;

        #endregion


        #region part attribute calculations
        private float GetMassSafely()
		{
            return avionicsDensity > 0 ? GetShieldedAvionicsMass() : 0;
        }

		private float GetCostSafely()
		{
            return avionicsDensity > 0 ? GetAvionicsCost() : 0;
        }

		#endregion

		private void UpdateControllableMassSlider()
		{
			if (controllableMassEdit == null) {
				controllableMassEdit = (UI_FloatEdit)Fields[nameof(controllableMass)].uiControlEditor;
			}

            if (CurrentProceduralAvionicsConfig != null && CurrentProceduralAvionicsTechNode != null)
            {
                controllableMassEdit.maxValue = CeilingToSmallIncrement(GetMaximumControllableMass());
                controllableMassEdit.minValue = 0;

                controllableMassEdit.incrementSmall = GetSmallIncrement(controllableMassEdit.maxValue);
                controllableMassEdit.incrementLarge = controllableMassEdit.incrementSmall * 10;
                controllableMassEdit.incrementSlide = GetSliderIncrement(controllableMassEdit.maxValue);
                controllableMassEdit.sigFigs = GetSigFigs(controllableMassEdit.maxValue);
            }
            else
            {
                Log("WARNING: Cannot update max value yet, CurrentProceduralAvionicsConfig is null");
            }
		}

        private int GetSigFigs(float value)
        {
            var smallIncrementExponent = GetSmallIncrementExponent(value);
            return Math.Max(1 - (int)smallIncrementExponent, 0);
        }

        private float CeilingToSmallIncrement(float value)
        {
            var smallIncrement = GetSmallIncrement(value);
            return Mathf.Ceil(value / smallIncrement) * smallIncrement;
        }

        private float FloorToSliderIncrement(float value)
        {
            float sliderIncrement = GetSliderIncrement(value);
            return Mathf.Floor(value / sliderIncrement) * sliderIncrement;
        }

        private float GetSliderIncrement(float value)
        {
            var smallIncrement = GetSmallIncrement(value);
            return Math.Min(smallIncrement / 10, 1f);
        }

        private float GetSmallIncrement(float value)
        {
            var exponent = GetSmallIncrementExponent(value);
            return (float) Math.Pow(10, exponent);
        }

        private double GetSmallIncrementExponent(float maxValue)
        {
            var log = Math.Log(maxValue, 10);
            return Math.Max(Math.Floor(log - 1.3), -2);
        }

        private void UpdateConfigSliders()
		{
			Log("Updating Config Slider");
			var avionicsConfigField = Fields[nameof(avionicsConfigName)];
			avionicsConfigField.guiActiveEditor = true;
			var range = (UI_ChooseOption)avionicsConfigField.uiControlEditor;
			range.options = ProceduralAvionicsTechManager.GetPurchasedConfigs().ToArray();

			if (string.IsNullOrEmpty(avionicsConfigName)) {
				avionicsConfigName = range.options[0];
				Log("Defaulted config to ", avionicsConfigName);
			}
		}

		private float cachedVolume = float.MaxValue;
		private BaseEventDetails cachedEventData = null;

		[KSPEvent]
		public void OnPartVolumeChanged(BaseEventDetails eventData)
		{
			Log("OnPartVolumeChanged called");
			if (!started) {
				Log("Not yet started, returning");
				cachedEventData = eventData;
				return;
			}
			try
            {
                float volume = (float)eventData.Get<double>("newTotalVolume");
                Log("volume changed to ", volume);
                if (volume * FLOAT_TOLERANCE < cachedMinVolume && cachedMinVolume != float.MaxValue)
                {
                    Log("volume of ", volume, " is less than expected min volume of ", cachedMinVolume, " expecting another update");
                    RefreshPartWindow();
                    //assuming the part will be resized
                    return;
                }
                Log("setting cachedVolume to ", volume);
                cachedVolume = volume;
                SendRemainingVolume();
                UpdateControllableMassSlider();
                RefreshDisplays();
            }
            catch (Exception ex) {
				Log("error getting changed volume: ", ex);
			}
		}

        private void SendRemainingVolume()
        {
            if(cachedVolume == float.MaxValue)
            {
                return;
            }
            Log($"Sending remaining volume: {cachedVolume - GetAvionicsMass() / avionicsDensity}");
            Events[nameof(OnPartVolumeChanged)].active = false;
            SendVolumeChangedEvent(cachedVolume - GetAvionicsMass() / avionicsDensity * InternalTanksVolumeUtilization);
            Events[nameof(OnPartVolumeChanged)].active = true;
        }

        public void SendVolumeChangedEvent(double newVolume)
        {
            var data = new BaseEventDetails(BaseEventDetails.Sender.USER);
            data.Set<string>("volName", "Tankage");
            data.Set<double>("newTotalVolume", newVolume);
            part.SendEvent(nameof(OnPartVolumeChanged), data, 0);
        }

        private void SetInternalKSPFields()
        {
            Log("Setting internal KSP fields");
            Log("avionics tech level: ", avionicsTechLevel);

            massExponent = CurrentProceduralAvionicsTechNode.massExponent;
            massConstant = CurrentProceduralAvionicsTechNode.massConstant;
            massFactor = CurrentProceduralAvionicsTechNode.massFactor;
            shieldingMassFactor = CurrentProceduralAvionicsTechNode.shieldingMassFactor;
            costExponent = CurrentProceduralAvionicsTechNode.costExponent;
            costConstant = CurrentProceduralAvionicsTechNode.costConstant;
            costFactor = CurrentProceduralAvionicsTechNode.costFactor;
            powerExponent = CurrentProceduralAvionicsTechNode.powerExponent;
            powerConstant = CurrentProceduralAvionicsTechNode.powerConstant;
            powerFactor = CurrentProceduralAvionicsTechNode.powerFactor;
            disabledPowerFactor = CurrentProceduralAvionicsTechNode.disabledPowerFactor;
            avionicsDensity = CurrentProceduralAvionicsTechNode.avionicsDensity;

            SASServiceLevel = CurrentProceduralAvionicsTechNode.SASServiceLevel;
            hasScienceContainer = CurrentProceduralAvionicsTechNode.hasScienceContainer;
            interplanetary = CurrentProceduralAvionicsTechNode.interplanetary;
        }

        private void RefreshDisplays()
        {
            RefreshCostAndMassDisplays();

            utilizationDisplay = String.Format("{0:0.#}%", Utilization* 100);
            Log("Utilization display: ", utilizationDisplay);

            RefreshPowerDisplay();
        }

        public float Utilization => GetAvionicsMass() / MaxAvionicsMass;

        private float MaxAvionicsMass => cachedVolume * avionicsDensity;

        private void RefreshPowerDisplay()
        {
            StringBuilder powerConsumptionBuilder = StringBuilderCache.Acquire();
            double kW = GetEnabledkW();
            if (kW >= 1)
            {
                powerConsumptionBuilder.AppendFormat(kwFormat, kW).Append(" kW");
            }
            else
            {
                powerConsumptionBuilder.AppendFormat(wFormat, kW * 1000).Append(" W");
            }
            double dkW = GetDisabledkW();
            if (dkW > 0)
            {
                powerConsumptionBuilder.Append(" /");
                if (dkW >= 0.1)
                {
                    powerConsumptionBuilder.AppendFormat(kwFormat, dkW).Append(" kW");
                }
                else
                {
                    powerConsumptionBuilder.AppendFormat(wFormat, dkW * 1000).Append(" W");
                }
            }

            powerRequirementsDisplay = powerConsumptionBuilder.ToStringAndRelease();
        }

        private ModuleSAS sasModule = null;
		private void SetSASServiceLevel()
		{
			if (SASServiceLevel >= 0) {
				if (sasModule == null) {
					sasModule = part.FindModuleImplementing<ModuleSAS>();
				}
				if (sasModule != null) {
					if (sasModule.SASServiceLevel != SASServiceLevel) {
						sasModule.SASServiceLevel = SASServiceLevel;
						Log("Setting SAS service level to ", SASServiceLevel);
					}
				}
			}
		}

		private bool scienceContainerFiltered = false;
		private void SetScienceContainer()
		{
			if (scienceContainerFiltered) {
				return;
			}
			if (!hasScienceContainer) {
				var module = part.FindModuleImplementing<ModuleScienceContainer>();
				if (module != null) {
					part.RemoveModule(module);
				}
			}
			Log("Setting science container to ", (hasScienceContainer ? "enabled." : "disabled."));
			scienceContainerFiltered = true;
		}

        private void RefreshCostAndMassDisplays()
		{
			massDisplay = MathUtils.FormatMass(GetMassSafely());
			costDisplay = Mathf.Round(GetCostSafely()).ToString();
		}

        #region Config GUI
        [KSPField(isPersistant = false, guiActiveEditor = true, guiActive = false, guiName = "Configure"),
		UI_Toggle(enabledText = "Hide GUI", disabledText = "Show GUI"),
		NonSerialized]
		public bool showGUI;

		private Rect windowRect = new Rect(200, Screen.height - 400, 400, 300);

		public void OnGUI()
		{
			if (showGUI) {
				windowRect = GUILayout.Window(GetInstanceID(), windowRect, WindowFunction, "Configure Procedural Avionics");
			}
		}

		private int selectedConfigIndex = 0;
		void WindowFunction(int windowID)
		{
			var configNames = ProceduralAvionicsTechManager.GetAvailableConfigs().ToArray();
			selectedConfigIndex = GUILayout.Toolbar(selectedConfigIndex, configNames);
			var guiAvionicsConfigName = configNames[selectedConfigIndex];
			var currentlyDisplayedConfigs = ProceduralAvionicsTechManager.GetProceduralAvionicsConfig(guiAvionicsConfigName);
			foreach (var techNode in currentlyDisplayedConfigs.TechNodes.Values) {
				if (!techNode.IsAvailable) {
					continue;
				}
				if (techNode == CurrentProceduralAvionicsTechNode) {
					GUILayout.Label("Current Config: " + techNode.name);
					GUILayout.Label("SAS Level: " + techNode.SASServiceLevel.ToString());
					GUILayout.Label("Storage Container: " + (techNode.hasScienceContainer ? "Yes" : "No"));
				}
				else {
					var switchedConfig = false;
					var unlockCost = ProceduralAvionicsTechManager.GetUnlockCost(guiAvionicsConfigName, techNode);
					if (unlockCost == 0) {
						if (GUILayout.Button("Switch to " + BuildTechName(techNode))) {
							switchedConfig = true;
						}
					}
					else if (Funding.Instance.Funds < unlockCost) {
						GUILayout.Label("Can't afford " + BuildTechName(techNode) + BuildCostString(unlockCost));
					}
					else if (GUILayout.Button("Purchase " + BuildTechName(techNode) + BuildCostString(unlockCost))) {
						switchedConfig = true;
						if (!HighLogic.CurrentGame.Parameters.Difficulty.BypassEntryPurchaseAfterResearch) {
							switchedConfig = ProceduralAvionicsTechManager.PurchaseConfig(guiAvionicsConfigName, techNode);
						}
						if (switchedConfig) {
							ProceduralAvionicsTechManager.SetMaxUnlockedTech(guiAvionicsConfigName, techNode.name);
						}

					}
					if (switchedConfig) {
						Log("Configuration window changed, updating part window");
						UpdateConfigSliders();
						avionicsTechLevel = techNode.name;
						currentProceduralAvionicsConfig = currentlyDisplayedConfigs;
						avionicsConfigName = guiAvionicsConfigName;
						AvionicsConfigChanged();
                    }
				}
			}
			GUILayout.Label(" ");
			if (GUILayout.Button("Close")) {
				showGUI = false;
			}

			GUI.DragWindow();
		}

		private string BuildTechName(ProceduralAvionicsTechNode techNode)
		{
			StringBuilder sbuilder = StringBuilderCache.Acquire();
			sbuilder.Append(techNode.name);
			sbuilder.Append(BuildSasAndScienceString(techNode));

			return sbuilder.ToStringAndRelease();
		}

		private static string BuildSasAndScienceString(ProceduralAvionicsTechNode techNode)
		{
			StringBuilder sbuilder = StringBuilderCache.Acquire();
			sbuilder.Append(" {SAS: ");
			sbuilder.Append(techNode.SASServiceLevel);
			if (techNode.hasScienceContainer) {
				sbuilder.Append(", SC");
			}
			sbuilder.Append("}");

			return sbuilder.ToString();
		}

		private string BuildCostString(int cost)
		{
			if (cost == 0 || HighLogic.CurrentGame.Parameters.Difficulty.BypassEntryPurchaseAfterResearch) {
				return String.Empty;
			}
			return " (" + String.Format("{0:N}", cost) + ")";
		}

		#endregion

		private void RefreshPartWindow()
		{
			UIPartActionWindow[] partWins = FindObjectsOfType<UIPartActionWindow>();
			foreach (UIPartActionWindow partWin in partWins) {
				partWin.displayDirty = true;
			}
		}
	}
}
