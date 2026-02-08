using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using vJoyInterfaceWrap;

namespace XJoy
{
	public partial class MainForm : Form
	{
		public class DeviceListItem
		{
			public string DisplayName;
			public Guid DeviceGuid;
			public byte DeviceIndex;

			public DeviceListItem(string name, Guid guid, byte index)
			{
				DisplayName = name;
				DeviceGuid = guid;
				DeviceIndex = index;
			}

			public override string ToString()
			{
				return DisplayName;
			}
		}

		public struct InputMapping
		{
			public enum OutputType
			{
				Axis,
				Button,
				Pov,
			}

			public int DeviceSlot;
			public DirectInputManager.Inputs Input;
			public OutputType Type;
			public HID_USAGES Axis;
			public uint ButtonIndex;
			public uint PovIndex;

			public InputMapping(int deviceSlot, DirectInputManager.Inputs input, HID_USAGES axis)
			{
				DeviceSlot = deviceSlot;
				Input = input;
				Type = OutputType.Axis;
				Axis = axis;
				ButtonIndex = 0;
				PovIndex = 0;
			}

			public InputMapping(int deviceSlot, DirectInputManager.Inputs input, uint buttonIndex, bool isPov)
			{
				DeviceSlot = deviceSlot;
				Input = input;
				Type = isPov ? OutputType.Pov : OutputType.Button;
				Axis = HID_USAGES.HID_USAGE_X;
				ButtonIndex = isPov ? 0 : buttonIndex;
				PovIndex = isPov ? buttonIndex : 0;
			}
		}

		public struct InputDefinitionEx
		{
			public int DeviceSlot;
			public DirectInputManager.InputDefinition Def;
			public string DisplayName;
		}

		public struct NullOutput
		{
			public override string ToString()
			{
				return "<None>";
			}
		}

		DirectInputManager diInputObj;
		DirectInputManager diInputObj2;
		vJoyManager vJoyObj;
		bool bIsActive = false;
		bool hidGuardianWhitelisted = false;
		bool showLiveValues = false;
		const int ColumnGroupCount = 3;

		List<DeviceListItem> DirectInputDevices = new List<DeviceListItem>();
		List<bool> ActiveVJoyControllers = new List<bool>();

		List<InputMapping> InputMappings = new List<InputMapping>();
		List<InputDefinitionEx> inputDefinitionsEx;

		Thread feederThread = null;
		DataGridView mappingGrid;
		List<DirectInputManager.InputDefinition> inputDefinitions;

		public MainForm()
		{
			InitializeComponent();
			try
			{
				label1.Text = "DirectInput Device A";
				this.Text = "XJoy - DirectInput to vJoy";

				diInputObj = new DirectInputManager();
				diInputObj2 = new DirectInputManager();
				vJoyObj = new vJoyManager();
				hidGuardianWhitelisted = HidGuardianHelper.TryInsertCurrentProcessToWhiteList();

				SetupMappingGrid();
				RefreshDeviceList();

				Application.ApplicationExit += new EventHandler(delegate (Object o, EventArgs a)
				{
					StopThread();
					if (hidGuardianWhitelisted)
						HidGuardianHelper.TryRemoveCurrentProcessFromWhiteList();

					if (MainNotifyIcon != null)
					{
						MainNotifyIcon.Icon = null;
						MainNotifyIcon.Dispose();
						MainNotifyIcon = null;
					}
				});

				string[] cmds = Environment.GetCommandLineArgs();
				HandleArguments(cmds);
			}
			catch (System.IO.FileNotFoundException e)
			{
				MessageBox.Show("FileNotFoundException: " + e.FileName + ". This may occur because of missing DLLs. Make sure to include everything! Application will exit..", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				Environment.Exit(-1);
			}
			catch (Exception e)
			{
				MessageBox.Show("Error: " + e.Message + ". This may occur because of missing DLLs. Make sure to include everything! Application will exit..", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				Environment.Exit(-1);
			}
		}

		private void SetupMappingGrid()
		{
			mappingGrid = new DataGridView();
			mappingGrid.Dock = DockStyle.Fill;
			mappingGrid.AllowUserToAddRows = false;
			mappingGrid.AllowUserToDeleteRows = false;
			mappingGrid.RowHeadersVisible = false;
			mappingGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
			mappingGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
			mappingGrid.MultiSelect = false;
			mappingGrid.BackgroundColor = RemappingPanel.BackColor;
			mappingGrid.BorderStyle = BorderStyle.None;
			mappingGrid.ScrollBars = ScrollBars.Both;

			for (int group = 0; group < ColumnGroupCount; group++)
			{
				var inputCol = new DataGridViewTextBoxColumn();
				inputCol.HeaderText = "DirectInput";
				inputCol.ReadOnly = true;
				inputCol.SortMode = DataGridViewColumnSortMode.NotSortable;
				inputCol.Width = 160;

				var outputCol = new DataGridViewComboBoxColumn();
				outputCol.HeaderText = "vJoy Output";
				outputCol.DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton;
				outputCol.FlatStyle = FlatStyle.Flat;
				outputCol.Width = 140;

				var valueCol = new DataGridViewTextBoxColumn();
				valueCol.HeaderText = "Value";
				valueCol.ReadOnly = true;
				valueCol.SortMode = DataGridViewColumnSortMode.NotSortable;
				valueCol.Width = 80;

				mappingGrid.Columns.Add(inputCol);
				mappingGrid.Columns.Add(outputCol);
				mappingGrid.Columns.Add(valueCol);

				var spacerCol = new DataGridViewTextBoxColumn();
				spacerCol.HeaderText = "";
				spacerCol.ReadOnly = true;
				spacerCol.SortMode = DataGridViewColumnSortMode.NotSortable;
				spacerCol.Width = 12;
				mappingGrid.Columns.Add(spacerCol);
			}

			mappingGrid.CellValueChanged += MappingGrid_CellValueChanged;
			mappingGrid.CurrentCellDirtyStateChanged += MappingGrid_CurrentCellDirtyStateChanged;
			mappingGrid.DataError += MappingGrid_DataError;

			RemappingPanel.Controls.Clear();
			RemappingPanel.Controls.Add(mappingGrid);

			inputDefinitions = DirectInputManager.GetInputDefinitions().ToList();
			inputDefinitionsEx = new List<InputDefinitionEx>();
			for (int slot = 0; slot < 2; slot++)
			{
				foreach (var def in inputDefinitions)
				{
					var displayName = (slot == 0 ? "A: " : "B: ") + def.Name;
					var defEx = new InputDefinitionEx { DeviceSlot = slot, Def = def, DisplayName = displayName };
					inputDefinitionsEx.Add(defEx);
				}
			}

			int rows = (int)Math.Ceiling(inputDefinitionsEx.Count / (double)ColumnGroupCount);
			for (int r = 0; r < rows; r++)
			{
				var rowIndex = mappingGrid.Rows.Add();
				var row = mappingGrid.Rows[rowIndex];
				for (int group = 0; group < ColumnGroupCount; group++)
				{
					int idx = (group * rows) + r;
					int colBase = group * 4;
					var outputCell = row.Cells[colBase + 1] as DataGridViewComboBoxCell;
					if (idx < inputDefinitionsEx.Count)
					{
						var defEx = inputDefinitionsEx[idx];
						row.Cells[colBase].Value = defEx.DisplayName;
						if (outputCell != null)
						{
							outputCell.Value = "<None>";
							outputCell.Tag = defEx;
							outputCell.ReadOnly = false;
							outputCell.DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton;
						}
						row.Cells[colBase + 2].Value = "";
					}
					else
					{
						row.Cells[colBase].Value = "";
						if (outputCell != null)
						{
							outputCell.Value = "";
							outputCell.Tag = null;
							outputCell.ReadOnly = true;
							outputCell.DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing;
						}
						row.Cells[colBase + 2].Value = "";
					}
					row.Cells[colBase + 3].Value = "";
				}
			}
		}

				private void MappingGrid_DataError(object sender, DataGridViewDataErrorEventArgs e)
		{
			// Reset invalid values to avoid default error dialog.
			var cell = mappingGrid.Rows[e.RowIndex].Cells[e.ColumnIndex] as DataGridViewComboBoxCell;
			if (cell != null && cell.Items.Count > 0)
				cell.Value = cell.Items[0];
			e.ThrowException = false;
		}

private void MappingGrid_CurrentCellDirtyStateChanged(object sender, EventArgs e)
		{
			if (mappingGrid.IsCurrentCellDirty)
				mappingGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
		}

		private void MappingGrid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
		{
			if (e.ColumnIndex < 0)
				return;
			// Output columns are the second column in each group (index 1,5,9,...).
			if (e.ColumnIndex % 4 == 1)
				UpdateInputMappings();
		}

		private void HandleArguments(string[] Args)
		{
			string fullCmd = "";
			for (int i = 0; i < Args.Length; i++)
			{
				fullCmd += Args[i] + " ";
			}
			fullCmd.TrimEnd(' ');
			Console.WriteLine("Cmd: " + fullCmd);

			for (int i = 0; i < Args.Length; i++)
			{
				HandleSingleArgument(Args, i);
			}
		}

		private void HandleSingleArgument(string[] Args, int Index)
		{
			if (Index < Args.Length)
			{
				bool indexIsLast = (Index == Args.Length - 1);
				string cmd = Args[Index];
				string cmd2 = "";
				if (!indexIsLast) cmd2 = Args[Index + 1];

				switch (cmd)
				{
					case "-c":
						{
							if (!indexIsLast)
							{
								Console.WriteLine("Detected config file: '" + cmd2 + "'");
								ReadConfigFile(cmd2);
							}
							break;
						}
					case "-s":
						{
							Console.WriteLine("Starting in silent mode (minimized).");
							this.WindowState = FormWindowState.Minimized;
							this.ShowInTaskbar = false;
							break;
						}
					case "-a":
						{
							if ((diInputObj.IsDeviceActive || diInputObj2.IsDeviceActive) && comboVJoyDevices.SelectedItem != null)
								Console.WriteLine("Auto-running feeder.");
							else
								Console.WriteLine("Could not auto-run; no valid controllers selected.");
							break;
						}
					default:
						break;
				}
			}
		}

		private void ReadConfigFile(string FName)
		{
			if (File.Exists(FName))
			{
				FileStream FS = File.Open(FName, FileMode.Open, FileAccess.Read);
				StreamReader FSR = new StreamReader(FS);

				Console.WriteLine("---- CONFIG START ----");
				string Line = null;
				while ((Line = FSR.ReadLine()) != null)
				{
					string[] CfgLine = Line.Split('=');
					if (CfgLine.Length == 2)
					{
						string left = CfgLine[0].ToLower();
						string right = CfgLine[1].ToLower();

						if (left == "directinput" || left == "di" || left == "xinput")
						{
							if (right == "any")
							{
								if (comboDevices.Items.Count > 0)
									comboDevices.SelectedIndex = 0;
								Console.WriteLine("Set DirectInput controller to first valid index.");
							}
							else
							{
								int index = -1;
								bool s = Int32.TryParse(right, out index);
								if (s)
								{
									if (index < comboDevices.Items.Count)
									{
										comboDevices.SelectedIndex = index;
										Console.WriteLine("Set DirectInput controller to #" + index);
									}
									else
									{
										Console.WriteLine("Invalid DirectInput controller index #" + index);
									}
								}
							}
						}
												else if (left == "directinput2" || left == "di2")
						{
							if (right == "any")
							{
								if (comboDevices2.Items.Count > 0)
									comboDevices2.SelectedIndex = 0;
								Console.WriteLine("Set DirectInput controller 2 to first valid index.");
							}
							else
							{
								int index = -1;
								bool s = Int32.TryParse(right, out index);
								if (s)
								{
									if (index < comboDevices2.Items.Count)
									{
										comboDevices2.SelectedIndex = index;
										Console.WriteLine("Set DirectInput controller 2 to #" + index);
									}
									else
									{
										Console.WriteLine("Invalid DirectInput controller 2 index #" + index);
									}
								}
							}
						}
else if (left == "vjoy")
						{
							if (right == "any")
							{
								if (comboVJoyDevices.Items.Count > 0)
									comboVJoyDevices.SelectedIndex = 0;
								Console.WriteLine("Set vJoy controller to first valid index.");
							}
							else
							{
								int index = -1;
								bool s = Int32.TryParse(right, out index);
								if (s)
								{
									if (index < ActiveVJoyControllers.Count && ActiveVJoyControllers[index])
									{
										ComboBox.ObjectCollection items = comboVJoyDevices.Items;
										for (int i = 0; i < items.Count; i++)
										{
											DeviceListItem dli = items[i] as DeviceListItem;
											if (dli != null && dli.DeviceIndex == index)
											{
												comboVJoyDevices.SelectedIndex = i;
												Console.WriteLine("Set vJoy controller to #" + index);
												break;
											}
										}
									}
									else
									{
										Console.WriteLine("Invalid vJoy controller index #" + index);
									}
								}
							}
						}
						else
						{
							ParseConfigMapping(left, right);
						}
					}
				}
				Console.WriteLine("----  CONFIG END  ----");

				FSR.Close();
			}
			else
			{
				Console.WriteLine("Could not find config file '" + FName + "'");
			}
		}

		private void ParseConfigMapping(string From, string To)
		{
			var cell = GetOutputCellForInput(From);
			if (cell == null)
			{
				Console.WriteLine("Failed to find input row for '" + From + "'");
				return;
			}

			object output;
			if (TryGetOutputFromString(To, out output))
			{
				foreach (var item in cell.Items)
				{
					if (string.Equals(item.ToString(), output.ToString(), StringComparison.OrdinalIgnoreCase))
					{
						cell.Value = item;
						return;
					}
				}
				Console.WriteLine("Failed to find output mapping for '" + To + "'");
			}
			else
			{
				Console.WriteLine("Failed to parse output mapping for '" + To + "'");
			}
		}

		private DataGridViewComboBoxCell GetOutputCellForInput(string From)
		{
			string lower = From.ToLower().Trim();
			string noPrefix = lower;
			if (lower.StartsWith("a:") || lower.StartsWith("b:"))
				noPrefix = lower.Substring(2).Trim();

			foreach (DataGridViewRow row in mappingGrid.Rows)
			{
				for (int group = 0; group < ColumnGroupCount; group++)
				{
					int colBase = group * 4;
					var inputCell = row.Cells[colBase];
					var outputCell = row.Cells[colBase + 1] as DataGridViewComboBoxCell;
					if (inputCell == null || outputCell == null || inputCell.Value == null)
						continue;

					var cellText = inputCell.Value.ToString().ToLower().Trim();
					if (cellText == lower || cellText == noPrefix)
						return outputCell;
				}
			}
			return null;
		}

		private bool TryGetOutputFromString(string input, out object output)
		{
			output = null;
			string lower = input.ToLower().Trim();

			HID_USAGES axis;
			if (TryGetAxisFromString(lower, out axis))
			{
				output = new vJoyManager.AnalogInput(axis, vJoyManager.AxisToFriendlyName(axis));
				return true;
			}

			if (lower.StartsWith("pov"))
			{
				var digits = lower.Replace("pov", string.Empty).Replace("#", string.Empty).Trim();
				int povIndex;
				if (Int32.TryParse(digits, out povIndex))
				{
					output = new vJoyManager.PovInput((uint)povIndex, "POV #" + povIndex);
					return true;
				}
			}

			if (lower.StartsWith("button"))
			{
				var digits = lower.Replace("button", string.Empty).Replace("#", string.Empty).Trim();
				int buttonIndex;
				if (Int32.TryParse(digits, out buttonIndex))
				{
					output = new vJoyManager.DigitalInput((uint)buttonIndex, "Button #" + buttonIndex);
					return true;
				}
			}

			int directButtonIndex;
			if (Int32.TryParse(lower, out directButtonIndex))
			{
				output = new vJoyManager.DigitalInput((uint)directButtonIndex, "Button #" + directButtonIndex);
				return true;
			}

			return false;
		}

		private bool TryGetAxisFromString(string inputLower, out HID_USAGES Axis)
		{
			Axis = HID_USAGES.HID_USAGE_X;
			var normalized = inputLower.Replace("axis", string.Empty).Trim();
			switch (normalized)
			{
				case "x":
					Axis = HID_USAGES.HID_USAGE_X;
					break;
				case "y":
					Axis = HID_USAGES.HID_USAGE_Y;
					break;
				case "z":
					Axis = HID_USAGES.HID_USAGE_Z;
					break;
				case "rx":
					Axis = HID_USAGES.HID_USAGE_RX;
					break;
				case "ry":
					Axis = HID_USAGES.HID_USAGE_RY;
					break;
				case "rz":
					Axis = HID_USAGES.HID_USAGE_RZ;
					break;
				case "sl0":
				case "slider":
				case "slider1":
					Axis = HID_USAGES.HID_USAGE_SL0;
					break;
				case "sl1":
				case "dial/slider 2":
				case "slider2":
					Axis = HID_USAGES.HID_USAGE_SL1;
					break;
				case "whl":
				case "wheel":
					Axis = HID_USAGES.HID_USAGE_WHL;
					break;
				default:
					return false;
			}
			return true;
		}

		private void RefreshMappingGridOutputs()
		{
			var outputs = new List<string>();
			outputs.Add("<None>");

			if (comboVJoyDevices.SelectedItem != null)
			{
				DeviceListItem vJoyItem = comboVJoyDevices.SelectedItem as DeviceListItem;
				if (vJoyItem != null)
				{
					List<HID_USAGES> vJoyAxes = vJoyObj.GetExistingAxes(vJoyItem.DeviceIndex);
					int vJoyButtonCount = vJoyObj.GetButtonCount(vJoyItem.DeviceIndex);
					int vJoyPovCount = vJoyObj.GetPovCount(vJoyItem.DeviceIndex);

					foreach (HID_USAGES axis in vJoyAxes)
						outputs.Add(vJoyManager.AxisToFriendlyName(axis));

					for (uint i = 1; i < vJoyButtonCount + 1; i++)
						outputs.Add("Button #" + i);

					for (uint i = 1; i < vJoyPovCount + 1; i++)
						outputs.Add("POV #" + i);
				}
			}

			foreach (DataGridViewRow row in mappingGrid.Rows)
			{
				for (int group = 0; group < ColumnGroupCount; group++)
				{
					int colBase = group * 4;
					RefreshOutputCell(row.Cells[colBase + 1] as DataGridViewComboBoxCell, outputs);
				}
			}
		}

		static void RefreshOutputCell(DataGridViewComboBoxCell cell, List<string> outputs)
		{
			if (cell == null)
				return;
			if (cell.Tag == null)
			{
				cell.Items.Clear();
				cell.Value = "";
				return;
			}

			string current = cell.Value as string;
			cell.Value = outputs[0];
			cell.Items.Clear();
			foreach (var item in outputs)
				cell.Items.Add(item);

			if (!string.IsNullOrEmpty(current))
			{
				var match = outputs.FirstOrDefault(o => string.Equals(o, current, StringComparison.OrdinalIgnoreCase));
				cell.Value = match ?? outputs[0];
			}
			else
			{
				cell.Value = outputs[0];
			}
		}

		private void UpdateInputMappings()
		{
			InputMappings.Clear();

			foreach (DataGridViewRow row in mappingGrid.Rows)
			{
				for (int group = 0; group < ColumnGroupCount; group++)
				{
					int colBase = group * 4;
					UpdateInputMappingFromCell(row.Cells[colBase + 1] as DataGridViewComboBoxCell);
				}
			}
		}

		void UpdateInputMappingFromCell(DataGridViewComboBoxCell cell)
		{
			if (cell == null || cell.Value == null || cell.Tag == null)
				return;

			var defEx = (InputDefinitionEx)cell.Tag;
			var valueText = cell.Value as string;
			if (string.IsNullOrEmpty(valueText) || valueText == "<None>")
				return;

			object output;
			if (TryGetOutputFromString(valueText, out output))
			{
				if (output is vJoyManager.AnalogInput analog)
					InputMappings.Add(new InputMapping(defEx.DeviceSlot, defEx.Def.Input, analog.Axis));
				else if (output is vJoyManager.DigitalInput digital)
					InputMappings.Add(new InputMapping(defEx.DeviceSlot, defEx.Def.Input, digital.ButtonIndex, false));
				else if (output is vJoyManager.PovInput pov)
					InputMappings.Add(new InputMapping(defEx.DeviceSlot, defEx.Def.Input, pov.PovIndex, true));
			}
		}

		private void SetActiveState(bool bActive)
		{
			if (bActive)
			{
				if ((diInputObj.IsDeviceActive || diInputObj2.IsDeviceActive) && comboVJoyDevices.SelectedItem != null)
				{
					DeviceListItem vJoyItem = comboVJoyDevices.SelectedItem as DeviceListItem;
					if (vJoyObj != null)
					{
						vJoyObj.InitDevice(vJoyItem.DeviceIndex);
						if (vJoyObj.IsDeviceAcquired)
						{
							buttonActivate.Text = "Deactivate";
							panelControls.Enabled = false;
							bIsActive = true;
							StartThread();
						}
						else
						{
							SetInfoText("Failed to acquire vJoy device.", Color.Red);
						}
					}
					else
					{
						SetInfoText("vJoy device appears to be invalid.", Color.Red);
					}
				}
			}
			else
			{
				bIsActive = false;
				buttonActivate.Enabled = false;
				StopThread();
				vJoyObj.ReleaseDevice();
				buttonActivate.Enabled = ((diInputObj.IsDeviceActive || diInputObj2.IsDeviceActive) && comboVJoyDevices.SelectedItem != null);
				buttonActivate.Text = "Activate";
				panelControls.Enabled = true;
				SetInfoText("Select devices and press Activate to enable.", Color.Black);
			}
		}

		private void StartThread()
		{
			if (feederThread != null)
			{
				StopThread();
			}

			feederThread = new Thread(new ThreadStart(this.ThreadFunc));
			feederThread.Start();
		}

		private void StopThread()
		{
			if (feederThread != null)
			{
				SetInfoText("Stopping feeder thread.", Color.DarkCyan);
				bool threadDead = feederThread.Join(2000);
				if (!threadDead)
				{
					SetInfoText("Stopping feeder thread.", Color.Red);
					feederThread.Abort();
					feederThread.Join();
				}
			}
		}

		private int RemapValueToVJoy(int Value, int XMin, int XMax, vJoyManager.AxisExtents vJoyLimit)
		{
			float value = (float)vJoyLimit.Min + ((float)Value - ((float)XMin)) / (((float)XMax) - ((float)XMin)) * ((float)vJoyLimit.Max - (float)vJoyLimit.Min);
			return (int)Math.Round(value);
		}

		public void ThreadFunc()
		{
			try
			{
				this.BeginInvoke((Action)(() =>
				{
					SetInfoText("Running feeder.", Color.Green);
				}));

				DirectInputManager.InputState stateData;
			DirectInputManager.InputState stateData2;

				List<bool> finalButtonStates = new List<bool>();
				int ButtonCount = vJoyObj.GetButtonCount(vJoyObj.ActiveVJoyID);
				while (finalButtonStates.Count < ButtonCount) finalButtonStates.Add(false);

				Dictionary<HID_USAGES, int> finalHidValues = new Dictionary<HID_USAGES, int>();
				Dictionary<HID_USAGES, vJoyManager.AxisExtents> HidExtents = new Dictionary<HID_USAGES, vJoyManager.AxisExtents>();
				List<HID_USAGES> AvailableHidValues = vJoyObj.GetExistingAxes(vJoyObj.ActiveVJoyID);
				foreach (HID_USAGES axis in AvailableHidValues)
				{
					finalHidValues.Add(axis, 0);
					HidExtents.Add(axis, vJoyObj.GetAxisExtents(axis));
				}

				var povCounts = vJoyObj.GetPovCounts(vJoyObj.ActiveVJoyID);
				int povCount = Math.Max(povCounts.cont, povCounts.disc);
				int[] finalPovValues = new int[povCount];
				for (int i = 0; i < povCount; i++)
					finalPovValues[i] = -1;

				int tempHidValue = 0;
				var lastValueUpdate = DateTime.MinValue;
				while (bIsActive)
				{
					stateData = diInputObj.GetState();
					stateData2 = diInputObj2.GetState();

					for (int i = 0; i < finalButtonStates.Count; i++)
						finalButtonStates[i] = false;

					for (int i = 0; i < AvailableHidValues.Count; i++)
						finalHidValues[AvailableHidValues[i]] = 0;

					for (int i = 0; i < finalPovValues.Length; i++)
						finalPovValues[i] = -1;

					foreach (InputMapping inputMap in InputMappings)
					{
						var inputKind = DirectInputManager.GetInputKind(inputMap.Input);
						var state = inputMap.DeviceSlot == 0 ? stateData : stateData2;
						switch (inputMap.Type)
						{
							case InputMapping.OutputType.Button:
								if (inputKind == DirectInputManager.InputKind.Button)
								{
									bool pressed = DirectInputManager.GetButtonValue(ref state, inputMap.Input);
									if (inputMap.ButtonIndex > 0 && inputMap.ButtonIndex <= finalButtonStates.Count)
										finalButtonStates[(int)inputMap.ButtonIndex - 1] |= pressed;
								}
								break;
							case InputMapping.OutputType.Axis:
								if (inputKind == DirectInputManager.InputKind.Axis)
								{
									tempHidValue = DirectInputManager.GetAxisValue(ref state, inputMap.Input);
									var range = DirectInputManager.GetAxisLimitsForInput(inputMap.Input);
									tempHidValue = RemapValueToVJoy(tempHidValue, range[0], range[1], HidExtents[inputMap.Axis]);
									finalHidValues[inputMap.Axis] = Math.Max(finalHidValues[inputMap.Axis], tempHidValue);
								}
								break;
							case InputMapping.OutputType.Pov:
								if (inputKind == DirectInputManager.InputKind.Pov)
								{
									int povValue = DirectInputManager.GetPovValue(ref state, inputMap.Input);
									if (inputMap.PovIndex > 0 && inputMap.PovIndex <= (uint)finalPovValues.Length)
										finalPovValues[inputMap.PovIndex - 1] = povValue;
								}
								break;
							default:
								break;
						}
					}

					for (uint i = 0; i < finalButtonStates.Count; i++)
						vJoyObj.SetButton(i + 1, finalButtonStates[(int)i]);

					for (int i = 0; i < AvailableHidValues.Count; i++)
					{
						HID_USAGES Axis = AvailableHidValues[i];
						vJoyObj.SetAxis(Axis, finalHidValues[Axis]);
					}

					for (uint i = 0; i < finalPovValues.Length; i++)
					{
						var value = finalPovValues[i];
						if (povCounts.disc > 0 && povCounts.cont == 0)
						{
							vJoyObj.SetDiscPov(i + 1, ConvertPovToDisc(value));
						}
						else
						{
							vJoyObj.SetPov(i + 1, value);
						}
					}

					if (showLiveValues && (DateTime.UtcNow - lastValueUpdate).TotalMilliseconds >= 100)
					{
						lastValueUpdate = DateTime.UtcNow;
						UpdateValueCells(stateData, stateData2);
					}

					Thread.Sleep(16);
				}
			}
			catch (ThreadAbortException)
			{
			}
		}

		void ClearValueCells()
		{
			if (mappingGrid.IsDisposed)
				return;
			if (mappingGrid.InvokeRequired)
			{
				mappingGrid.BeginInvoke((Action)(ClearValueCells));
				return;
			}
			foreach (DataGridViewRow row in mappingGrid.Rows)
			{
				for (int group = 0; group < ColumnGroupCount; group++)
				{
					int colBase = group * 4;
					var valueCell = row.Cells[colBase + 2];
					if (valueCell != null)
						valueCell.Value = "";
				}
			}
		}

void UpdateValueCells(DirectInputManager.InputState stateA, DirectInputManager.InputState stateB)
		{
			if (mappingGrid.IsDisposed)
				return;

			if (mappingGrid.InvokeRequired)
			{
				mappingGrid.BeginInvoke((Action)(() => UpdateValueCells(stateA, stateB)));
				return;
			}

			foreach (DataGridViewRow row in mappingGrid.Rows)
			{
				for (int group = 0; group < ColumnGroupCount; group++)
				{
					int colBase = group * 4;
					var outputCell = row.Cells[colBase + 1] as DataGridViewComboBoxCell;
					var valueCell = row.Cells[colBase + 2];
					if (outputCell == null || outputCell.Tag == null || valueCell == null)
						continue;

					var defEx = (InputDefinitionEx)outputCell.Tag;
					var state = defEx.DeviceSlot == 0 ? stateA : stateB;
					var kind = DirectInputManager.GetInputKind(defEx.Def.Input);
					switch (kind)
					{
						case DirectInputManager.InputKind.Axis:
							valueCell.Value = DirectInputManager.GetAxisValue(ref state, defEx.Def.Input).ToString();
							break;
						case DirectInputManager.InputKind.Pov:
							valueCell.Value = DirectInputManager.GetPovValue(ref state, defEx.Def.Input).ToString();
							break;
						case DirectInputManager.InputKind.Button:
							valueCell.Value = DirectInputManager.GetButtonValue(ref state, defEx.Def.Input) ? "1" : "0";
							break;
					}
				}
			}
		}

		static int ConvertPovToDisc(int value)
		{
			// DirectInput POV: -1 neutral, 0=up, 9000=right, 18000=down, 27000=left.
			if (value < 0)
				return -1;
			switch (value)
			{
				case 0:
					return 0;
				case 9000:
					return 1;
				case 18000:
					return 2;
				case 27000:
					return 3;
				default:
					// Round to nearest 90 degrees.
					int snapped = (int)Math.Round(value / 9000.0) % 4;
					return snapped;
			}
		}

		public void SetInfoText(string text, Color color)
		{
			labelInfo.Text = text;
			labelInfo.ForeColor = color;
		}

		private void RefreshUIState()
		{
			if ((!diInputObj.IsDeviceActive && !diInputObj2.IsDeviceActive) || comboVJoyDevices.SelectedItem == null)
			{
				SetActiveState(false);
				buttonActivate.Enabled = false;
			}
			else
			{
				buttonActivate.Enabled = true;
			}

			RefreshMappingGridOutputs();
		}

		private void RefreshDeviceList()
		{
			comboDevices.Items.Clear();
			comboDevices2.Items.Clear();
			DirectInputDevices.Clear();
			ActiveVJoyControllers.Clear();

			var devices = diInputObj.GetDevices();
			for (int i = 0; i < devices.Count; i++)
			{
				var d = devices[i];
				var item = new DeviceListItem($"DirectInput: {d.InstanceName}", d.InstanceGuid, (byte)i);
				DirectInputDevices.Add(item);
				comboDevices.Items.Add(item);
			comboDevices2.Items.Add(item);
			}

			comboVJoyDevices.Items.Clear();
			bool[] activeVJoyDevices = vJoyObj.GetAvailableIndices();
			ActiveVJoyControllers.AddRange(activeVJoyDevices);
			for (int i = 0; i < activeVJoyDevices.Length; i++)
			{
				if (activeVJoyDevices[i])
				{
					comboVJoyDevices.Items.Add(new DeviceListItem("vJoy Controller #" + i, Guid.Empty, (byte)i));
				}
			}

			RefreshUIState();
		}

		private void MainForm_Resize(object sender, EventArgs e)
		{
			if (this.WindowState == FormWindowState.Minimized)
			{
				this.ShowInTaskbar = false;
			}
			else if (this.WindowState == FormWindowState.Normal)
			{
				this.ShowInTaskbar = true;
				this.BringToFront();
			}
		}

		private void showXJoyWindowToolStripMenuItem_Click(object sender, EventArgs e)
		{
			this.BringToFront();
			this.ShowInTaskbar = true;
			this.WindowState = FormWindowState.Normal;
		}

		private void exitToolStripMenuItem_Click(object sender, EventArgs e)
		{
			StopThread();
			Application.Exit();
		}

		private void buttonInfo_Click(object sender, EventArgs e)
		{
			InfoWindow iw = new InfoWindow();
			iw.ShowDialog();
		}

		private void MainNotifyIcon_DoubleClick(object sender, EventArgs e)
		{
			this.WindowState = FormWindowState.Normal;
			this.ShowInTaskbar = true;
			this.BringToFront();
		}

		private void buttonActivate_Click(object sender, EventArgs e)
		{
			SetActiveState(!bIsActive);
		}

		private void comboDevices_SelectedIndexChanged(object sender, EventArgs e)
		{
			DeviceListItem item = comboDevices.SelectedItem as DeviceListItem;
			if (item != null)
			{
				diInputObj.InitDevice(item.DeviceGuid);
				RefreshUIState();
			}
		}

				private void comboDevices2_SelectedIndexChanged(object sender, EventArgs e)
		{
			DeviceListItem item = comboDevices2.SelectedItem as DeviceListItem;
			if (item != null)
			{
				diInputObj2.InitDevice(item.DeviceGuid);
				RefreshUIState();
			}
		}

private void comboVJoyDevices_SelectedIndexChanged(object sender, EventArgs e)
		{
			DeviceListItem item = comboVJoyDevices.SelectedItem as DeviceListItem;
			if (item != null)
			{
				RemappingPanel.Enabled = true;
				RefreshUIState();
			}
		}

		private void buttonRefresh_Click(object sender, EventArgs e)
		{
			RefreshDeviceList();
		}

		private void checkBoxShowValues_CheckedChanged(object sender, EventArgs e)
		{
			showLiveValues = checkBoxShowValues.Checked;
			if (!showLiveValues)
				ClearValueCells();
		}

private void onInputMappingChanged(object sender, EventArgs e)
		{
			UpdateInputMappings();
		}

		private void buttonClearMapping_Click(object sender, EventArgs e)
		{
			foreach (DataGridViewRow row in mappingGrid.Rows)
			{
				var cell = row.Cells[1] as DataGridViewComboBoxCell;
				if (cell == null)
					continue;
				if (cell.Items.Count > 0)
					cell.Value = cell.Items[0];
			}
		}

		private void buttonLoadMapping_Click(object sender, EventArgs e)
		{
			DialogResult dr = openFileDialogMapping.ShowDialog();
			if (dr == DialogResult.OK)
			{
				ReadConfigFile(openFileDialogMapping.FileName);
			}
		}

		private void buttonSaveMapping_Click(object sender, EventArgs e)
		{
			DialogResult dr = saveFileDialogMapping.ShowDialog();
			if (dr == DialogResult.OK)
			{
				bool useSelectedDI = false;
				bool useSelectedDI2 = false;
				bool useSelectedVJoy = false;
				DeviceListItem selectedvJoy = comboVJoyDevices.SelectedItem as DeviceListItem;

				if (diInputObj.IsDeviceActive)
				{
					useSelectedDI = GenericPrompt.DoPrompt("DirectInput Device A", "Use selected DirectInput device A or any available device?", "Selected", "Any");
				}

				if (diInputObj2.IsDeviceActive)
				{
					useSelectedDI2 = GenericPrompt.DoPrompt("DirectInput Device B", "Use selected DirectInput device B or any available device?", "Selected", "Any");
				}

				if (selectedvJoy != null)
				{
					useSelectedVJoy = GenericPrompt.DoPrompt("vJoy Controller", "Use selected vJoy controller index or any available controller?", "Selected", "Any");
				}

				StreamWriter wr = new StreamWriter(saveFileDialogMapping.FileName, false);

				string diA = (useSelectedDI && comboDevices.SelectedItem is DeviceListItem) ? ((DeviceListItem)comboDevices.SelectedItem).DeviceIndex.ToString() : "any";
				string diB = (useSelectedDI2 && comboDevices2.SelectedItem is DeviceListItem) ? ((DeviceListItem)comboDevices2.SelectedItem).DeviceIndex.ToString() : "any";
				string vjoy = (useSelectedVJoy && comboVJoyDevices.SelectedItem is DeviceListItem) ? ((DeviceListItem)comboVJoyDevices.SelectedItem).DeviceIndex.ToString() : "any";

				wr.WriteLine("DirectInput=" + diA);
				wr.WriteLine("DirectInput2=" + diB);
				wr.WriteLine("vJoy=" + vjoy);

				foreach (DataGridViewRow row in mappingGrid.Rows)
				{
					for (int group = 0; group < ColumnGroupCount; group++)
					{
						int colBase = group * 4;
						var inputCell = row.Cells[colBase];
						var cell = row.Cells[colBase + 1] as DataGridViewComboBoxCell;
						if (inputCell == null || inputCell.Value == null || cell == null || cell.Value == null)
							continue;

						var inputName = inputCell.Value.ToString();
						var outputName = cell.Value as string;
						if (string.IsNullOrEmpty(outputName) || string.Equals(outputName, "<None>", StringComparison.OrdinalIgnoreCase))
							continue;

						var normalized = outputName;
						if (normalized.StartsWith("Button", StringComparison.OrdinalIgnoreCase))
							normalized = normalized.Replace("Button", "").Replace("#", "").Trim();
						if (normalized.StartsWith("POV", StringComparison.OrdinalIgnoreCase))
							normalized = "POV" + normalized.Replace("POV", "").Replace("#", "").Trim();

						wr.WriteLine(inputName + "=" + normalized);
					}
				}

				wr.Close();
			}
		}
    }
}
