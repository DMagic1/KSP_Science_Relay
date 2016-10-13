#region license
/*The MIT License (MIT)

ScienceRelay - MonoBehaviour for controlling the transfer of science from one vessel to another

Copyright (c) 2016 DMagic

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/
#endregion

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using KSP.UI.TooltipTypes;
using KSP.UI.Screens.Flight.Dialogs;
using CommNet;
using FinePrint.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace ScienceRelay
{
	[KSPAddon(KSPAddon.Startup.Flight, false)]
	public class ScienceRelay : MonoBehaviour
    {
		private static Sprite transferNormal;
		private static Sprite transferHighlight;
		private static Sprite transferActive;
		private static bool spritesLoaded;
		private static ScienceRelay instance;

		private string version;
		private ScienceRelayParameters settings;
		private bool transferAll;
		private PopupDialog transferDialog;
		private PopupDialog warningDialog;
		private Button transferButton;
		private ExperimentsResultDialog resultsDialog;
		private ExperimentResultDialogPage currentPage;
		private List<ScienceRelayData> queuedData = new List<ScienceRelayData>();
		private CommPath pathCache = new CommPath();
		private List<KeyValuePair<Vessel, double>> connectedVessels = new List<KeyValuePair<Vessel, double>>();
		private static MethodInfo _occlusionMethod;
		private static bool reflected;

		public static ScienceRelay Instance
		{
			get { return instance; }
		}

		private void Awake()
		{
			if (HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX)
				Destroy(gameObject);

			if (instance != null)
				Destroy(gameObject);

			if (!reflected)
				assignReflection();

			if (!spritesLoaded)
				loadSprite();

			instance = this;
			
			processPrefab();
		}

		private void Start()
		{
			ScienceRelayDialog.onDialogSpawn.Add(onSpawn);
			ScienceRelayDialog.onDialogClose.Add(onClose);
			GameEvents.OnTriggeredDataTransmission.Add(onTriggeredData);
			GameEvents.onGamePause.Add(onPause);
			GameEvents.onGameUnpause.Add(onUnpause);

			settings = HighLogic.CurrentGame.Parameters.CustomParams<ScienceRelayParameters>();

			if (settings == null)
			{
				instance = null;
				Destroy(gameObject);
			}

			Assembly assembly = AssemblyLoader.loadedAssemblies.GetByAssembly(Assembly.GetExecutingAssembly()).assembly;
			var ainfoV = Attribute.GetCustomAttribute(assembly, typeof(AssemblyInformationalVersionAttribute)) as AssemblyInformationalVersionAttribute;
			switch (ainfoV == null)
			{
				case true: version = ""; break;
				default: version = ainfoV.InformationalVersion; break;
			}
		}

		private void OnDestroy()
		{
			instance = null;

			popupDismiss();

			ScienceRelayDialog.onDialogSpawn.Remove(onSpawn);
			ScienceRelayDialog.onDialogClose.Remove(onClose);
			GameEvents.OnTriggeredDataTransmission.Remove(onTriggeredData);
			GameEvents.onGamePause.Remove(onPause);
			GameEvents.onGameUnpause.Remove(onUnpause);
		}

		private void loadSprite()
		{
			Texture2D normal = GameDatabase.Instance.GetTexture("ScienceRelay/Resources/Relay_Normal", false);
			Texture2D highlight = GameDatabase.Instance.GetTexture("ScienceRelay/Resources/Relay_Highlight", false);
			Texture2D active = GameDatabase.Instance.GetTexture("ScienceRelay/Resources/Relay_Active", false);

			if (normal == null || highlight == null || active == null)
				return;

			transferNormal = Sprite.Create(normal, new Rect(0, 0, normal.width, normal.height), new Vector2(0.5f, 0.5f));
			transferHighlight = Sprite.Create(highlight, new Rect(0, 0, highlight.width, highlight.height), new Vector2(0.5f, 0.5f));
			transferActive = Sprite.Create(active, new Rect(0, 0, active.width, active.height), new Vector2(0.5f, 0.5f));

			spritesLoaded = true;
		}

		private void onPause()
		{
			if (transferDialog != null)
				transferDialog.gameObject.SetActive(false);

			if (warningDialog != null)
				warningDialog.gameObject.SetActive(false);
		}

		private void onUnpause()
		{
			if (transferDialog != null)
				transferDialog.gameObject.SetActive(true);

			if (warningDialog != null)
				warningDialog.gameObject.SetActive(true);
		}

		private void processPrefab()
		{
			GameObject prefab = AssetBase.GetPrefab("ScienceResultsDialog");

			if (prefab == null)
				return;

			ScienceRelayDialog dialogListener = prefab.gameObject.AddOrGetComponent<ScienceRelayDialog>();

			Button[] buttons = prefab.GetComponentsInChildren<Button>(true);

			for (int i = buttons.Length - 1; i >= 0; i--)
			{
				Button b = buttons[i];

				if (b.name == "ButtonPrev")
					dialogListener.buttonPrev = b;
				else if (b.name == "ButtonNext")
					dialogListener.buttonNext = b;
				else if (b.name == "ButtonKeep")
				{
					transferButton = Instantiate(b, b.transform.parent) as Button;

					transferButton.name = "ButtonTransfer";

					transferButton.onClick.RemoveAllListeners();

					TooltipController_Text tooltip = transferButton.GetComponent<TooltipController_Text>();

					if (tooltip != null)
						tooltip.textString = "Transfer Data To Another Vessel";

					if (spritesLoaded)
					{
						Selectable select = transferButton.GetComponent<Selectable>();

						if (select != null)
						{
							select.image.sprite = transferNormal;
							select.image.type = Image.Type.Simple;
							select.transition = Selectable.Transition.SpriteSwap;

							SpriteState state = select.spriteState;
							state.highlightedSprite = transferHighlight;
							state.pressedSprite = transferActive;
							state.disabledSprite = transferActive;
							select.spriteState = state;
						}
					}

					dialogListener.buttonTransfer = transferButton;
				}
			}
		}

		private void onSpawn(ExperimentsResultDialog dialog)
		{
			if (dialog == null)
				return;

			resultsDialog = dialog;

			var buttons = resultsDialog.GetComponentsInChildren<Button>(true);

			for (int i = buttons.Length - 1; i >= 0; i--)
			{
				Button b = buttons[i];

				if (b == null)
					continue;

				if (b.name != "ButtonTransfer")
					continue;

				transferButton = b;
				break;
			}

			currentPage = resultsDialog.currentPage;

			transferButton.gameObject.SetActive(getConnectedVessels());
		}

		private void onClose(ExperimentsResultDialog dialog)
		{
			if (dialog == null || resultsDialog == null)
				return;

			if (dialog == resultsDialog)
			{
				resultsDialog = null;
				transferButton = null;
				currentPage = null;
			}

			popupDismiss();
		}

		public void onPageChange()
		{
			if (resultsDialog == null)
				return;

			currentPage = resultsDialog.currentPage;

			popupDismiss();
		}

		public void onTransfer()
		{
			if (resultsDialog == null)
				return;

			if (currentPage == null)
				return;

			if (currentPage.pageData == null)
				return;

			if (connectedVessels.Count <= 0)
				return;

			transferAll = false;

			transferDialog = spawnDialog(currentPage);
		}

		private void popupDismiss()
		{
			if (transferDialog != null)
				transferDialog.Dismiss();
		}

		private PopupDialog spawnDialog(ExperimentResultDialogPage page)
		{
			List<DialogGUIBase> dialog = new List<DialogGUIBase>();

			dialog.Add(new DialogGUIHorizontalLayout(true, false, 0, new RectOffset(), TextAnchor.UpperCenter, new DialogGUIBase[]
				{
					new DialogGUILabel(string.Format("Transmit data to the selected vessel:\n{0}", page.pageData.title), false, false)
				}));

			transferAll = false;

			if (resultsDialog.pages.Count > 1)
			{
				dialog.Add(new DialogGUIHorizontalLayout(true, false, 0, new RectOffset(), TextAnchor.UpperCenter, new DialogGUIBase[]
				{
				new DialogGUIToggle(false, "Transfer All Open Data",
					delegate(bool b)
					{
						transferAll = !transferAll;
					}, 200, 20)
				}));
			}

			List<DialogGUIHorizontalLayout> vessels = new List<DialogGUIHorizontalLayout>();

			for (int i = connectedVessels.Count - 1; i >= 0; i--)
			{
				KeyValuePair<Vessel, double> pair = connectedVessels[i];

				Vessel v = pair.Key;
				
				float boost = signalBoost((float)pair.Value, v, currentPage.pageData, currentPage.xmitDataScalar);

				DialogGUILabel label = null;

				if (settings.transmissionBoost)
				{
					string transmit = string.Format("Xmit: {0:P0}", currentPage.xmitDataScalar * (1 + boost));

					if (boost > 0)
						transmit += string.Format("(+{0:P0})", boost);

					label = new DialogGUILabel(transmit, 130, 25);
				}

				DialogGUIBase button = null;

				if (settings.showTransmitWarning && currentPage.showTransmitWarning)
				{
					button = new DialogGUIButton(
											v.vesselName,
											delegate
											{
												spawnWarningDialog(
												new ScienceRelayData()
												{
													_data = currentPage.pageData,
													_host = currentPage.host,
													_boost = boost,
													_source = FlightGlobals.ActiveVessel,
													_target = v,
												},
												currentPage.transmitWarningMessage);
											},
											160,
											30,
											true,
											null);
				}
				else
				{
					button = new DialogGUIButton<ScienceRelayData>(
											v.vesselName,
											transferToVessel,
											new ScienceRelayData()
											{
												_data = currentPage.pageData,
												_host = currentPage.host,
												_boost = boost,
												_source = FlightGlobals.ActiveVessel,
												_target = v,
											},
											true);

					button.size = new Vector2(160, 30);
				}

				DialogGUIHorizontalLayout h = new DialogGUIHorizontalLayout(true, false, 4, new RectOffset(), TextAnchor.MiddleCenter, new DialogGUIBase[] { button });

				if (label != null)
					h.AddChild(label);

				vessels.Add(h);
			}

			DialogGUIBase[] scrollList = new DialogGUIBase[vessels.Count + 1];

			scrollList[0] = new DialogGUIContentSizer(ContentSizeFitter.FitMode.Unconstrained, ContentSizeFitter.FitMode.PreferredSize, true);

			for (int i = 0; i < vessels.Count; i++)
				scrollList[i + 1] = vessels[i];

			dialog.Add(new DialogGUIScrollList(Vector2.one, false, true,
				new DialogGUIVerticalLayout(10, 100, 4, new RectOffset(6, 24, 10, 10), TextAnchor.MiddleLeft, scrollList)
				));

			dialog.Add(new DialogGUISpace(4));

			dialog.Add(new DialogGUIHorizontalLayout(new DialogGUIBase[]
			{ 
				new DialogGUIFlexibleSpace(),
				new DialogGUIButton("Cancel Transfer", popupDismiss),
				new DialogGUIFlexibleSpace(),
				new DialogGUILabel(version, false, false)
			}));

			RectTransform resultRect = resultsDialog.GetComponent<RectTransform>();

			Rect pos = new Rect(0.5f, 0.5f, 300, 300);

			if (resultRect != null)
			{
				Vector2 resultPos = resultRect.position;

				float scale = GameSettings.UI_SCALE;

				int width = Screen.width;
				int height = Screen.height;
				
				float xpos = (resultPos.x / scale) + width / 2;
				float ypos = (resultPos.y / scale) + height / 2;

				float yNorm = ypos / height;

				pos.y = yNorm;

				pos.x = xpos > (width - (550 * scale)) ? (xpos - 360) / width : (xpos + 360) / width;
			}
			
			return PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new MultiOptionDialog("", "Science Relay", UISkinManager.defaultSkin, pos, dialog.ToArray()), false, UISkinManager.defaultSkin);
		}

		private void spawnWarningDialog(ScienceRelayData data, string message)
		{
			warningDialog = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new MultiOptionDialog(
				message + "\nAre you sure you want to continue",
				"Warning!",
				UISkinManager.defaultSkin,
				new Rect(0.5f, 0.5f, 250, 120),
				new DialogGUIBase[]
				{
					new DialogGUIButton<ScienceRelayData>(
						"Transmit Data",
						transferToVessel,
						data,
						true
						),
					new DialogGUIButton("Cancel", null, true)
				}),
				false, UISkinManager.defaultSkin, true, "");
		}

		private float signalBoost(float s, Vessel target, ScienceData data, float xmit)
		{
			float f = 0;

			if (target == null)
				return f;

			if (settings.requireMPLForBoost && !VesselUtilities.VesselHasModuleName("ModuleScienceLab", target))
				return f;

			if (s <= 0)
				return f;

			if (data == null)
				return f;

			ScienceSubject sub = ResearchAndDevelopment.GetSubjectByID(data.subjectID);

			if (sub == null)
				return f;

			float recoveredData = ResearchAndDevelopment.GetScienceValue(data.dataAmount, sub, 1);
			float transmitData = ResearchAndDevelopment.GetScienceValue(data.dataAmount, sub, xmit);

			if (recoveredData <= 0)
				return f;

			if (transmitData <= 0)
				return f;

			if (transmitData * s > recoveredData)
				f = recoveredData / transmitData;
			else
				f = s;

			f -= 1;

			f = (1 - settings.transmissionPenalty) * f;

			return f;
		}

		private void transferToVessel(ScienceRelayData RelayData)
		{
			if (resultsDialog != null)
				resultsDialog.Dismiss();

			if (RelayData._host == null || RelayData._data == null || RelayData._target == null || RelayData._source == null)
				return;

			List<ScienceRelayData> data = new List<ScienceRelayData>();

			if (transferAll)
			{
				for (int i = resultsDialog.pages.Count - 1; i >= 0; i--)
				{
					ExperimentResultDialogPage page = resultsDialog.pages[i];

					if (page == null)
						continue;

					if (page.pageData == null)
						continue;

					if (page.host == null)
						continue;

					ScienceRelayData relayData = new ScienceRelayData()
						{
							_data = page.pageData,
							_host = page.host,
							_boost = signalBoost(RelayData._boost + 1, RelayData._target, page.pageData, page.xmitDataScalar),
							_target = RelayData._target,
							_source = RelayData._source,
						};

					relayData._data.baseTransmitValue = page.xmitDataScalar;

					data.Add(relayData);
				}
			}
			else
			{
				RelayData._data.baseTransmitValue = currentPage.xmitDataScalar;
				data.Add(RelayData);
			}

			for (int i = data.Count - 1; i >= 0; i--)
			{
				ScienceData d = data[i]._data;

				Part host = data[i]._host;

				List<IScienceDataContainer> containers = host.FindModulesImplementing<IScienceDataContainer>();

				IScienceDataContainer hostContainer = null;

				for (int j = containers.Count - 1; j >= 0; j--)
				{
					hostContainer = null;

					IScienceDataContainer container = containers[j];

					if (container == null)
						continue;

					ScienceData[] containerData = container.GetData();

					for (int k = containerData.Length - 1; k >= 0; k--)
					{
						ScienceData dat = containerData[k];

						if (dat.subjectID == d.subjectID)
						{
							hostContainer = container;
							break;
						}
					}

					if (hostContainer != null)
						break;
				}

				IScienceDataTransmitter bestTransmitter = ScienceUtil.GetBestTransmitter(RelayData._source.FindPartModulesImplementing<IScienceDataTransmitter>());

				if (bestTransmitter == null)
				{
					if (CommNetScenario.CommNetEnabled)
						ScreenMessages.PostScreenMessage("No usable, in-range Comms Devices on this vessel. Cannot Transmit Data.", 3, ScreenMessageStyle.UPPER_CENTER);
					else
						ScreenMessages.PostScreenMessage("No usable Comms Devices on this vessel. Cannot Transmit Data.", 3, ScreenMessageStyle.UPPER_CENTER);
				}
				else
				{
					d.triggered = true;

					bestTransmitter.TransmitData(new List<ScienceData> { d });

					queuedData.Add(data[i]);

					if (hostContainer != null)
						hostContainer.DumpData(d);
				}
			}
		}

		private void onTriggeredData(ScienceData data, Vessel vessel, bool aborted)
		{
			if (vessel == null)
				return;

			if (vessel != FlightGlobals.ActiveVessel)
				return;

			if (data == null)
				return;

			if (aborted)
				return;

			for (int i = queuedData.Count - 1; i >= 0; i--)
			{
				ScienceRelayData d = queuedData[i];

				if (d._data.subjectID != data.subjectID)
					continue;

				if (!finishTransfer(d._target, d._data, d._boost))
				{
					Part host = d._host;

					List<IScienceDataContainer> containers = host.FindModulesImplementing<IScienceDataContainer>();

					IScienceDataContainer hostContainer = null;

					for (int j = containers.Count - 1; j >= 0; j--)
					{
						IScienceDataContainer container = containers[j];

						if (container == null)
							continue;

						PartModule mod = container as PartModule;

						if (mod.part == null)
							continue;

						if (mod.part.flightID != data.container)
							continue;

						hostContainer = container;
						break;
					}

					if (hostContainer != null)
					{
						data.triggered = false;
						hostContainer.ReturnData(data);
					}
				}
				else
				{
					ScreenMessages.PostScreenMessage(string.Format("<color=#99FF00FF>[{0}] {1:N1} data received on <i>{2}</i></color>", d._target.vesselName, data.dataAmount, data.title), 4, ScreenMessageStyle.UPPER_LEFT);
				}

				queuedData.Remove(d);

				break;
			}
		}

		private bool finishTransfer(Vessel v, ScienceData d, float boost)
		{
			if (v == null)
				return false;

			if (d == null)
				return false;

			if (v.loaded)
			{
				List<ModuleScienceContainer> containers = v.FindPartModulesImplementing<ModuleScienceContainer>();

				if (containers.Count <= 0)
					return false;

				ModuleScienceContainer currentContainer = null;

				for (int j = containers.Count - 1; j >= 0; j--)
				{
					ModuleScienceContainer container = containers[j];

					if (container.capacity != 0 && container.GetData().Length >= container.capacity)
						continue;

					if (container.allowRepeatedSubjects)
					{
						currentContainer = container;
						break;
					}

					if (container.HasData(d))
						continue;

					currentContainer = container;
				}

				if (currentContainer != null)
				{
					d.triggered = false;
					d.dataAmount *= (d.baseTransmitValue * (1 + boost));
					d.transmitBonus = 1;
					d.baseTransmitValue = 1;
					return currentContainer.AddData(d);
				}
			}
			else
			{
				List<ProtoPartSnapshot> containers = getProtoContainers(v.protoVessel);

				if (containers.Count <= 0)
					return false;

				//RelayLog("Transfer {0}", 1);

				ProtoPartModuleSnapshot currentContainer = null;

				uint host = 0;

				for (int j = containers.Count - 1; j >= 0; j--)
				{
					//RelayLog("Transfer {0}", 2);

					ProtoPartSnapshot container = containers[j];

					host = container.flightID;

					ProtoPartModuleSnapshot tempContainer = null;

					for (int k = container.modules.Count - 1; k >= 0; k--)
					{
						ProtoPartModuleSnapshot mod = container.modules[k];

						if (mod.moduleName != "ModuleScienceContainer")
							continue;

						tempContainer = mod;

						//RelayLog("Transfer {0} - {1}", 3, tempContainer.moduleName);
						break;
					}

					if (tempContainer == null)
						continue;

					List<ScienceData> protoData = new List<ScienceData>();

					ConfigNode[] science = tempContainer.moduleValues.GetNodes("ScienceData");

					for (int l = science.Length - 1; l >= 0; l--)
					{
						ConfigNode node = science[l];

						protoData.Add(new ScienceData(node));
					}

					Part prefab = container.partInfo.partPrefab;

					ModuleScienceContainer prefabContainer = prefab.FindModuleImplementing<ModuleScienceContainer>();

					//RelayLog("Transfer {0}", 4);

					if (prefabContainer != null)
					{
						//RelayLog("Transfer {0}", 5);

						if (prefabContainer.capacity != 0 && protoData.Count >= prefabContainer.capacity)
							continue;

						if (prefabContainer.allowRepeatedSubjects)
						{
							//RelayLog("Transfer {0}", 6);
							currentContainer = tempContainer;
							break;
						}

						if (HasData(d.subjectID, protoData))
							continue;

						//RelayLog("Transfer {0} - {1}", 7, tempContainer.moduleName);

						currentContainer = tempContainer;
					}
				}

				if (currentContainer != null)
				{
					d.triggered = false;
					d.dataAmount = d.dataAmount * (d.baseTransmitValue * (boost + 1));
					d.transmitBonus = 1;
					d.baseTransmitValue = 1;
					d.container = host;
					//RelayLog("Transfer {0} - Data: {1} - Amount: {2:N4} - Boost: {3:N4}", 8, d.title, d.dataAmount, boost);
					d.Save(currentContainer.moduleValues.AddNode("ScienceData"));
					return true;
				}
			}

			return false;
		}

		private bool HasData(string id, List<ScienceData> data)
		{
			for (int i = data.Count - 1; i >= 0; i--)
			{
				ScienceData d = data[i];

				if (d.subjectID == id)
					return true;
			}

			return false;
		}

		private bool labWithCrew(Vessel v)
		{
			if (v == null || v.protoVessel == null)
				return false;

			if (v.loaded)
			{
				List<ModuleScienceLab> labs = v.FindPartModulesImplementing<ModuleScienceLab>();

				for (int i = labs.Count - 1; i >= 0; i--)
				{
					ModuleScienceLab lab = labs[i];

					if (lab == null)
						continue;

					if (lab.part.protoModuleCrew.Count >= lab.crewsRequired)
						return true;
				}
			}
			else
			{
				for (int i = v.protoVessel.protoPartSnapshots.Count - 1; i >= 0; i--)
				{
					ProtoPartSnapshot part = v.protoVessel.protoPartSnapshots[i];

					if (part == null)
						continue;

					for (int j = part.modules.Count - 1; j >= 0; j--)
					{
						ProtoPartModuleSnapshot mod = part.modules[j];

						if (mod == null)
							continue;

						if (mod.moduleName != "ModuleScienceLab")
							continue;

						int crew = (int)getCrewRequired(part);

						if (part.protoModuleCrew.Count >= crew)
							return true;
					}
				}
			}

			return false;
		}

		private float getCrewRequired(ProtoPartSnapshot part)
		{
			if (part == null)
				return 0;

			AvailablePart a = PartLoader.getPartInfoByName(part.partName);

			if (a == null)
				return 0;

			Part prefab = a.partPrefab;

			if (prefab == null)
				return 0;

			for (int i = prefab.Modules.Count - 1; i >= 0; i--)
			{
				PartModule mod = prefab.Modules[i];

				if (mod == null)
					continue;

				if (mod.moduleName != "ModuleScienceLab")
					continue;

				return ((ModuleScienceLab)mod).crewsRequired;
			}

			return 0;
		}

		private List<ProtoPartSnapshot> getProtoContainers(ProtoVessel v)
		{
			List<ProtoPartSnapshot> parts = new List<ProtoPartSnapshot>();

			for (int i = v.protoPartSnapshots.Count - 1; i >= 0; i--)
			{
				ProtoPartSnapshot p = v.protoPartSnapshots[i];

				if (p == null)
					continue;

				bool b = false;

				for (int j = p.modules.Count - 1; j >= 0; j--)
				{
					ProtoPartModuleSnapshot mod = p.modules[j];

					if (mod == null)
						continue;

					if (mod.moduleName == "ModuleScienceContainer")
					{
						parts.Add(p);
						b = true;
						break;
					}
				}

				if (b)
					break;
			}

			return parts;
		}

		private bool getConnectedVessels()
		{
			connectedVessels.Clear();

			if (resultsDialog == null)
				return false;

			Vessel vessel = FlightGlobals.ActiveVessel;

			if (vessel == null)
				return false;

			if (CommNetScenario.CommNetEnabled)
				connectedVessels = getConnectedVessels(vessel);
			else
			{
				for (int i = FlightGlobals.Vessels.Count - 1; i >= 0; i--)
				{
					Vessel v = FlightGlobals.Vessels[i];

					if (v == null)
						continue;

					if (v == vessel)
						continue;

					VesselType type = v.vesselType;

					if (type == VesselType.Debris || type == VesselType.SpaceObject || type == VesselType.Unknown || type == VesselType.Flag)
						continue;

					if (settings.requireMPL && !VesselUtilities.VesselHasModuleName("ModuleScienceLab", v))
						continue;

					if (!VesselUtilities.VesselHasModuleName("ModuleScienceContainer", v))
						continue;

					connectedVessels.Add(new KeyValuePair<Vessel, double>(v, 0));
				}
			}

			return connectedVessels.Count > 0;
		}

		private List<KeyValuePair<Vessel, double>> getConnectedVessels(Vessel v)
		{
			List<KeyValuePair<Vessel, double>> connections = new List<KeyValuePair<Vessel, double>>();

			List<KeyValuePair<CommNode, double>> checkNodes = new List<KeyValuePair<CommNode, double>>();

			//RelayLog("Vessels {0}", 0);

			if (v.connection != null)
			{
				CommNode source = v.connection.Comm;

				//RelayLog("Vessels {0}", 1);

				if (source != null)
				{
					checkNodes.Add(new KeyValuePair<CommNode, double>(source, 1));

					CommNetwork net = v.connection.Comm.Net;

					//RelayLog("Vessels {0}", 2);

					if (net != null)
					{
						//RelayLog("Vessels {0}", 3);

						for (int i = FlightGlobals.Vessels.Count - 1; i >= 0; i--)
						{
							Vessel otherVessel = FlightGlobals.Vessels[i];

							if (otherVessel == null)
								continue;

							if (otherVessel == v)
								continue;

							//RelayLog("Vessels {0} - OtherVessel - {1}", 4, otherVessel.vesselName);

							VesselType type = otherVessel.vesselType;

							if (type == VesselType.Debris || type == VesselType.SpaceObject || type == VesselType.Unknown || type == VesselType.Flag)
								continue;

							if (otherVessel.connection == null || otherVessel.connection.Comm == null)
								continue;

							//RelayLog("Vessels {0}", 5);

							if (!net.FindPath(source, pathCache, otherVessel.connection.Comm))
								continue;

							//RelayLog("Vessels {0}", 6);

							if (pathCache == null)
								continue;

							//RelayLog("Vessels {0}", 7);

							if (pathCache.Count <= 0)
								continue;

							double totalStrength = 1;

							int l = pathCache.Count;

							for (int j = 0; j < l; j++)
							{
								CommLink link = pathCache[j];

								totalStrength *= link.signalStrength;

								if (!link.a.isHome && !updateCommNode(checkNodes, link.a, totalStrength))
									checkNodes.Add(new KeyValuePair<CommNode, double>(link.a, totalStrength));

								if (!link.b.isHome && !updateCommNode(checkNodes, link.b, totalStrength))
									checkNodes.Add(new KeyValuePair<CommNode, double>(link.b, totalStrength));
							}

							if (settings.requireMPL && !VesselUtilities.VesselHasModuleName("ModuleScienceLab", otherVessel))
								continue;

							if (!VesselUtilities.VesselHasModuleName("ModuleScienceContainer", otherVessel))
								continue;

							//RelayLog("Vessels {0}", 9);

							double s = pathCache.signalStrength;

							s = source.scienceCurve.Evaluate(s);

							//RelayLog("Vessels {0} - Power: {1:N4} - Vessel: {2}", 9, s, otherVessel.vesselName);

							connections.Add(new KeyValuePair<Vessel, double>(otherVessel, s + 1));
						}

						//RelayLog("Vessels {0}", 10);

						for (int k = checkNodes.Count - 1; k >= 0; k--)
						{
							KeyValuePair<CommNode, double> pair = checkNodes[k];

							CommNode node = pair.Key;

							if (node.isHome)
								continue;

							//RelayLog("Vessels {0} - Source Node: {1}", 11, node.name);

							for (int l = FlightGlobals.Vessels.Count - 1; l >= 0; l--)
							{
								Vessel otherVessel = FlightGlobals.Vessels[l];

								if (otherVessel == null)
									continue;

								if (otherVessel == v)
									continue;

								//RelayLog("Vessels {0} - Direct Other Vessel: {1}", 12, otherVessel.vesselName);
								
								//RelayLog("Vessels {0}", 13);

								VesselType type = otherVessel.vesselType;

								if (type == VesselType.Debris || type == VesselType.SpaceObject || type == VesselType.Unknown || type == VesselType.Flag)
									continue;

								if (otherVessel.connection == null || otherVessel.connection.Comm == null)
									continue;

								CommNode otherComm = otherVessel.connection.Comm;

								//RelayLog("Vessels {0}", 14);

								if (otherComm.antennaRelay.power > 0)
									continue;

								if (settings.requireMPL && !VesselUtilities.VesselHasModuleName("ModuleScienceLab", otherVessel))
									continue;

								if (!VesselUtilities.VesselHasModuleName("ModuleScienceContainer", otherVessel))
									continue;

								//RelayLog("Vessels {0}", 15);

								double dist = (otherComm.precisePosition - node.precisePosition).magnitude;

								if (isOccluded(node, otherComm, dist, net))
									continue;

								//RelayLog("Vessels {0}", 16);

								double power = directConnection(node, otherComm, dist, source == node, pair.Value);

								if (power <= 0)
									continue;

								power = source.scienceCurve.Evaluate(power);

								//RelayLog("Vessels {0} - Power: {1:N4}", 18, power);

								bool flag = false;

								for (int m = connections.Count - 1; m >= 0; m--)
								{
									KeyValuePair<Vessel, double> connect = connections[m];

									if (connect.Key != otherVessel)
										continue;

									//RelayLog("Vessels {0} - Old Power: {1:N4}", 18, connect.Value);

									if (connect.Value < power + 1)
										connections[m] = new KeyValuePair<Vessel, double>(connect.Key, power + 1);

									flag = true;
									break;
								}

								for (int m = connections.Count - 1; m >= 0; m--)
								{
									KeyValuePair<Vessel, double> connect = connections[m];

									if (connect.Key != otherVessel)
										continue;

									//RelayLog("Vessels {0} - New Power: {1:N4}", 18, connect.Value);
									break;
								}

								if (!flag)
									connections.Add(new KeyValuePair<Vessel, double>(otherVessel, power + 1));
							}
						}
					}
				}
			}

			return connections;
		}

		private bool updateCommNode(List<KeyValuePair<CommNode, double>> nodes, CommNode newNode, double signal)
		{
			for (int i = nodes.Count - 1; i >= 0; i--)
			{
				KeyValuePair<CommNode, double> node = nodes[i];

				if (node.Key != newNode)
					continue;

				if (signal > node.Value)
					nodes[i] = new KeyValuePair<CommNode, double>(node.Key, signal);

				return true;
			}

			return false;
		}

		private bool isOccluded(CommNode a, CommNode b, double dist, CommNetwork net)
		{
			bool? occlusion = null;

			try
			{
				occlusion = _occlusionMethod.Invoke(
					net,
					new object[] { a.precisePosition, a.occluder, b.precisePosition, b.occluder, dist }
					) as bool?;
			}
			catch (Exception e)
			{
				RelayLog("Error in assessing occlusion for science relay...\n{0}", e);
				return true;
			}

			if (occlusion == null)
				return true;

			return !(bool)occlusion;
		}

		private double directConnection(CommNode a, CommNode b, double dist, bool source, double strength)
		{
			//RelayLog("Vessels {0} - Old Signal: {1:N4}", 17, strength);

			double plasmaMult = a.GetSignalStrengthMultiplier(b) * b.GetSignalStrengthMultiplier(a);

			double power = 0;

			//RelayLog("Vessels {0} - Plasma: {1:N4}", 17, plasmaMult);

			if (source)
			{
				double range = CommNetScenario.RangeModel.GetNormalizedRange(a.antennaTransmit.power, b.antennaTransmit.power, dist);

				//RelayLog("Source Vessels {0} - Range: {1:N4}", 17, range);

				if (range > 0)
				{
					power = Math.Sqrt(a.antennaTransmit.rangeCurve.Evaluate(range) * b.antennaTransmit.rangeCurve.Evaluate(range));

					//RelayLog("Source Vessels {0} - Power: {1:N4}", 17, power);

					power *= plasmaMult;
				}
				else
				{
					range = CommNetScenario.RangeModel.GetNormalizedRange(a.antennaRelay.power, b.antennaTransmit.power, dist);

					if (range > 0)
					{
						power = Math.Sqrt(a.antennaRelay.rangeCurve.Evaluate(range) * b.antennaTransmit.rangeCurve.Evaluate(range));

						power *= plasmaMult;
					}
				}
			}
			else
			{
				double range = CommNetScenario.RangeModel.GetNormalizedRange(a.antennaRelay.power, b.antennaTransmit.power, dist);

				//RelayLog("Vessels {0} - Range: {1:N4}", 17, range);

				if (range > 0)
				{
					power = Math.Sqrt(a.antennaRelay.rangeCurve.Evaluate(range) * b.antennaTransmit.rangeCurve.Evaluate(range));

					//RelayLog("Vessels {0} - Power: {1:N4}", 17, power);

					power *= plasmaMult;
				}
			}

			//RelayLog("Vessels {0} - Final Power: {1:N4}", 17, power * strength);

			return power * strength;
		}

		private void assignReflection()
		{
			_occlusionMethod = typeof(CommNetwork).GetMethod("TestOcclusion", BindingFlags.Instance | BindingFlags.NonPublic, null, new Type[] { typeof(Vector3d), typeof(Occluder), typeof(Vector3d), typeof(Occluder), typeof(double) }, null);

			reflected = true;
		}
		
		public static void RelayLog(string s, params object[] o)
		{
			Debug.Log(string.Format("[Science Relay] " + s, o));
		}
    }
}
