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
		private static Sprite transferImage;
		private static ScienceRelay instance;

		private string version;
		private ScienceRelayParameters settings;
		private bool transferAll;
		private PopupDialog transferDialog;
		private Button transferButton;
		private ExperimentsResultDialog resultsDialog;
		private ExperimentResultDialogPage currentPage;
		//private List<Vessel> connectedVessels = new List<Vessel>();
		private List<ScienceRelayData> queuedData = new List<ScienceRelayData>();
		private CommPath pathCache = new CommPath();
		private List<KeyValuePair<Vessel, double>> connectedVessels = new List<KeyValuePair<Vessel, double>>();

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

			instance = this;
			
			processPrefab();
		}

		private void Start()
		{
			ScienceRelayDialog.onDialogSpawn.Add(onSpawn);
			ScienceRelayDialog.onDialogClose.Add(onClose);
			GameEvents.OnTriggeredDataTransmission.Add(onTriggeredData);

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

					Image image = transferButton.GetComponent<Image>();

					//if (image != null)
					//image.sprite = transferImage;

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
				
				float boost = signalBoost((float)pair.Value, currentPage.pageData);

				DialogGUILabel label = null;

				if (settings.transmissionBoost)
					label = new DialogGUILabel(string.Format("Boost: +{0:P0}", boost), 110, 25);

				DialogGUIBase button = null;

				if (settings.showTransmitWarning && currentPage.showTransmitWarning)
				{
					button = new DialogGUIButton(
											v.vesselName,
											delegate
											{
												warningDialog(
												new ScienceRelayData()
												{
													_data = currentPage.pageData,
													_host = currentPage.host,
													_boost = boost,
													_source = FlightGlobals.ActiveVessel,
													_target = v,
												});
											},
											170,
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

					button.size = new Vector2(170, 30);
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

			return PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new MultiOptionDialog("", "Science Relay", UISkinManager.defaultSkin, new Rect(0.5f, 0.5f, 300, 300), dialog.ToArray()), false, UISkinManager.defaultSkin);
		}

		private void warningDialog(ScienceRelayData data)
		{
			PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new MultiOptionDialog(
				"\nAre you sure you want to continue",
				"Warning!",
				UISkinManager.defaultSkin,
				new Rect(0.5f, 0.5f, 300, 300),
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

		private float getSignalBoost(ScienceData data, Vessel from, Vessel to)
		{
			if (settings.transmissionBoost)
			{
				if (settings.requireMPLForBoost)
				{
					if (labWithCrew(to))
						return signalBoost(connectionStrength(from, to), data);
				}
				else
					return signalBoost(connectionStrength(from, to), data);
			}

			return 0;
		}

		private float connectionStrength(Vessel from, Vessel to)
		{
			float f = 0;

			if (!CommNetScenario.CommNetEnabled)
				return f;

			if (from == null || to == null)
				return f;

			RelayLog("Connection {0} - {1}", 0, to.vesselName);

			if (from.connection == null || from.connection.Comm == null || from.connection.Comm.Net == null || from.connection.Comm.scienceCurve == null)
				return f;

			if (to.connection == null | to.connection.Comm == null)
				return f;

			cachedTargetNode = to.connection.Comm;

			RelayLog("Connection {0}", 1);

			//from.connection.Comm.Net.con

			//if (from.connection.Comm.Net.FindClosestWhere(from.connection.Comm, pathCache, new Func<CommNode, CommNode, bool>(isTargetNode)) == null)
			//{
			//	cachedTargetNode = null;
			//	return f;
			//}

			if (!from.connection.Comm.Net.FindPath(from.connection.Comm, pathCache, to.connection.Comm))
			{
				cachedTargetNode = null;
				return f;
			}
			RelayLog("Connection {0}", 2);

			cachedTargetNode = null;

			if (pathCache == null)
				return f;

			RelayLog("Connection {0}", 3);

			if (pathCache.Count <= 0)
				return f;

			RelayLog("Connection {0} - Count {1}", 4, pathCache.Count);

			for (int i = pathCache.Count - 1; i >= 0; i--)
			{
				CommLink link = pathCache[i];

				RelayLog("Connection {0} - Link: {1} - A: {2} - B: {3}", 5, i, link.a.name, link.b.name);
			}

			if (!pathCache.Last.Contains(to.connection.Comm))
				return f;

			RelayLog("Connection {0}", 6);

			double s = pathCache.signalStrength;

			f = (float)from.connection.Comm.scienceCurve.Evaluate(s);

			RelayLog("Connection {0} - Value {1}", 7, f + 1);

			return f + 1;
		}

		private CommNode cachedTargetNode;

		private bool isTargetNode(CommNode source, CommNode target)
		{
			RelayLog("Connection Check - Relay: {0:N2} - Source: {1} - Target: {2} - IsHome: {3}", target.antennaRelay.power, source.name, target.name, target.isHome);

			return target.antennaRelay.power > 0 ? !target.isHome || target == cachedTargetNode : target == cachedTargetNode;
		}

		private bool isSourceNode(CommNode target, CommNode source)
		{
			RelayLog("Connection Check - Relay: {0:N2} - Source: {1} - Target: {2} - IsHome: {3}", source.antennaRelay.power, source.name, target.name, source.isHome);

			return source.antennaRelay.power > 0 ? true && !source.isHome: source == FlightGlobals.ActiveVessel.connection.Comm;

			//return source == FlightGlobals.ActiveVessel.connection.Comm;
		}

		private float signalBoost(float s, ScienceData data)
		{
			float f = 0;

			if (s <= 0)
				return f;

			if (data == null)
				return f;

			ScienceSubject sub = ResearchAndDevelopment.GetSubjectByID(data.subjectID);

			if (sub == null)
				return f;

			float recoveredData = ResearchAndDevelopment.GetScienceValue(data.dataAmount, sub, 1);
			float transmitData = ResearchAndDevelopment.GetScienceValue(data.dataAmount, sub, data.baseTransmitValue);

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

					data.Add(new ScienceRelayData()
						{
							_data = page.pageData,
							_host = page.host,
							_boost = signalBoost(RelayData._boost, page.pageData),
							_target = RelayData._target,
							_source = RelayData._source,
						});
				}
			}
			else
				data.Add(RelayData);

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

			RelayLog("Transfer {0}", 0);

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
					d.dataAmount *= (d.baseTransmitValue * boost);
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
					d.container = host;
					//RelayLog("Transfer {0} - {1}", 8, currentContainer.moduleName);
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
			{
				List<KeyValuePair<Vessel, double>> initialVesselList = getConnectedVessels(vessel);

				for (int i = initialVesselList.Count - 1; i >= 0; i--)
				{
					KeyValuePair<Vessel, double> pair = initialVesselList[i];

					if (settings.requireMPL && !VesselUtilities.VesselHasModuleName("ModuleScienceLab", pair.Key))
						continue;

					if (!VesselUtilities.VesselHasModuleName("ModuleScienceContainer", pair.Key))
						continue;

					connectedVessels.Add(new KeyValuePair<Vessel, double>(pair.Key, pair.Value));
				}
			}
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

			List<CommNode> checkNodes = new List<CommNode>();

			//RelayLog("Vessels {0}", 0);

			if (v.connection != null)
			{
				CommNode source = v.connection.Comm;

				//RelayLog("Vessels {0}", 1);

				if (source != null)
				{
					checkNodes.Add(source);

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

							for (int j = pathCache.Count - 1; j >= 0; j--)
							{
								CommLink link = pathCache[j];

								//RelayLog("Vessels {0} - Link {1}: A: {2} - B: {3}", 8, j, link.a.name, link.b.name);

								if (!checkNodes.Contains(link.a))
									checkNodes.Add(link.a);

								if (!checkNodes.Contains(link.b))
									checkNodes.Add(link.b);
							}

							if (settings.requireMPL && !VesselUtilities.VesselHasModuleName("ModuleScienceLab", otherVessel))
								continue;

							if (!VesselUtilities.VesselHasModuleName("ModuleScienceContainer", otherVessel))
								continue;

							//RelayLog("Vessels {0}", 9);

							double s = pathCache.signalStrength;

							s = source.scienceCurve.Evaluate(s);

							connections.Add(new KeyValuePair<Vessel, double>(otherVessel, s + 1));
						}

						//RelayLog("Vessels {0}", 10);

						for (int k = checkNodes.Count - 1; k >= 0; k--)
						{
							CommNode node = checkNodes[k];

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

								bool flag = false;

								for (int m = connections.Count - 1; m >= 0; m--)
								{
									Vessel vChecked = connections[m].Key;

									if (vChecked != otherVessel)
										continue;

									flag = true;
									break;
								}

								if (flag)
									continue;

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

								double power = directConnection(node, otherComm, dist, source == node);

								if (power <= 0)
									continue;

								//RelayLog("Vessels {0}", 18);

								power = source.scienceCurve.Evaluate(power);

								connections.Add(new KeyValuePair<Vessel, double>(otherVessel, power));
							}
						}


					}
				}
			}

			return connections;
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

		private void assignReflection()
		{
			_occlusionMethod = typeof(CommNetwork).GetMethod("TestOcclusion", BindingFlags.Instance | BindingFlags.NonPublic, null, new Type[] { typeof(Vector3d), typeof(Occluder), typeof(Vector3d), typeof(Occluder), typeof(double) }, null);

			reflected = true;
		}

		private static MethodInfo _occlusionMethod;
		private static bool reflected;

		private double directConnection(CommNode a, CommNode b, double dist, bool source)
		{
			double plasmaMult = a.GetSignalStrengthMultiplier(b) * b.GetSignalStrengthMultiplier(a);

			double power = 0;

			//RelayLog("Vessels {0} - Plasma: {1:N4}", 17, plasmaMult);

			if (source)
			{
				double range = CommNetScenario.RangeModel.GetNormalizedRange(a.antennaTransmit.power, b.antennaTransmit.power, dist);

				//RelayLog("Vessels {0} - Range: {1:N4}", 17, range);

				if (range > 0)
				{
					power = Math.Sqrt(a.antennaTransmit.rangeCurve.Evaluate(range) * b.antennaTransmit.rangeCurve.Evaluate(range));

					//RelayLog("Vessels {0} - Power: {1:N4}", 17, power);

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

			return power;
		}

		private bool vesselConnected(Vessel from, Vessel to)
		{
			CommNetwork network = from.connection.Comm.Net;

			if (network == null)
				return false;

			if (to == null || to.connection == null || to.connection.Comm == null)
				return false;

			return network.Contains(to.connection.Comm);
		}

		public static void RelayLog(string s, params object[] o)
		{
			Debug.Log(string.Format("[Science Relay] " + s, o));
		}
    }
}
