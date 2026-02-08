using System;
using System.Collections.Generic;
using System.Linq;
using SharpDX.DirectInput;

namespace XJoy
{
	public class DirectInputManager : IDisposable
	{
		public enum InputKind
		{
			Axis,
			Pov,
			Button,
		}

		public enum Inputs
		{
			Axis1 = 0,
			Axis2,
			Axis3,
			Axis4,
			Axis5,
			Axis6,
			Axis7,
			Axis8,
			Pov1,
			Pov2,
			Pov3,
			Pov4,
			Button1,
			Button2,
			Button3,
			Button4,
			Button5,
			Button6,
			Button7,
			Button8,
			Button9,
			Button10,
			Button11,
			Button12,
			Button13,
			Button14,
			Button15,
			Button16,
			Button17,
			Button18,
			Button19,
			Button20,
			Button21,
			Button22,
			Button23,
			Button24,
			Button25,
			Button26,
			Button27,
			Button28,
			Button29,
			Button30,
			Button31,
			Button32,
			Button33,
			Button34,
			Button35,
			Button36,
			Button37,
			Button38,
			Button39,
			Button40,
		}

		public const int AxisCount = 8;
		public const int PovCount = 4;
		public const int ButtonCount = 40;

		public struct InputDefinition
		{
			public Inputs Input;
			public string Name;
			public InputKind Kind;
			public int Index;
		}

		public struct InputState
		{
			public int[] Axes;
			public int[] Povs;
			public bool[] Buttons;
		}

		readonly DirectInput _directInput;
		Joystick _activeDevice;

		public DirectInputManager()
		{
			_directInput = new DirectInput();
		}

		public void Dispose()
		{
			ReleaseDevice();
			_directInput.Dispose();
		}

		public List<DeviceInstance> GetDevices()
		{
			var devices = new List<DeviceInstance>();
			devices.AddRange(_directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly));
			return devices;
		}

		public bool InitDevice(Guid instanceGuid)
		{
			ReleaseDevice();
			try
			{
				_activeDevice = new Joystick(_directInput, instanceGuid);
				_activeDevice.Properties.BufferSize = 32;
				_activeDevice.Acquire();
				return true;
			}
			catch
			{
				_activeDevice = null;
				return false;
			}
		}

		public void ReleaseDevice()
		{
			if (_activeDevice != null)
			{
				try
				{
					_activeDevice.Unacquire();
				}
				catch
				{
					// Ignore cleanup errors.
				}
				_activeDevice.Dispose();
				_activeDevice = null;
			}
		}

		public bool IsDeviceActive => _activeDevice != null;

		public InputState GetState()
		{
			var state = new InputState
			{
				Axes = new int[AxisCount],
				Povs = new int[PovCount],
				Buttons = new bool[ButtonCount],
			};

			for (int i = 0; i < PovCount; i++)
				state.Povs[i] = -1;

			if (_activeDevice == null)
				return state;

			try
			{
				_activeDevice.Poll();
				var js = _activeDevice.GetCurrentState();

				state.Axes[0] = js.X;
				state.Axes[1] = js.Y;
				state.Axes[2] = js.Z;
				state.Axes[3] = js.RotationX;
				state.Axes[4] = js.RotationY;
				state.Axes[5] = js.RotationZ;

				var sliders = js.Sliders ?? new int[0];
				state.Axes[6] = sliders.Length > 0 ? sliders[0] : 0;
				state.Axes[7] = sliders.Length > 1 ? sliders[1] : 0;

				var povs = js.PointOfViewControllers ?? new int[0];
				for (int i = 0; i < PovCount && i < povs.Length; i++)
					state.Povs[i] = povs[i];

				var buttons = js.Buttons ?? new bool[0];
				for (int i = 0; i < ButtonCount && i < buttons.Length; i++)
					state.Buttons[i] = buttons[i];
			}
			catch
			{
				// Ignore polling errors.
			}

			return state;
		}

		
		static string GetAxisName(int index)
		{
			switch (index)
			{
				case 0: return "Axis X";
				case 1: return "Axis Y";
				case 2: return "Axis Z";
				case 3: return "Axis Rx";
				case 4: return "Axis Ry";
				case 5: return "Axis Rz";
				case 6: return "Axis Slider1";
				case 7: return "Axis Slider2";
				default: return "Axis";
			}
		}

public static IReadOnlyList<InputDefinition> GetInputDefinitions()
		{
			var list = new List<InputDefinition>();
			for (int i = 0; i < AxisCount; i++)
			{
				list.Add(new InputDefinition
				{
					Input = (Inputs)i,
					Name = GetAxisName(i),
					Kind = InputKind.Axis,
					Index = i,
				});
			}
			for (int i = 0; i < PovCount; i++)
			{
				list.Add(new InputDefinition
				{
					Input = (Inputs)(AxisCount + i),
					Name = $"Pov{1 + i}",
					Kind = InputKind.Pov,
					Index = i,
				});
			}
			for (int i = 0; i < ButtonCount; i++)
			{
				list.Add(new InputDefinition
				{
					Input = (Inputs)(AxisCount + PovCount + i),
					Name = $"Button{1 + i}",
					Kind = InputKind.Button,
					Index = i,
				});
			}
			return list;
		}

		public static InputKind GetInputKind(Inputs input)
		{
			var idx = (int)input;
			if (idx < AxisCount)
				return InputKind.Axis;
			if (idx < AxisCount + PovCount)
				return InputKind.Pov;
			return InputKind.Button;
		}

		public static int[] GetAxisLimitsForInput(Inputs input)
		{
			var kind = GetInputKind(input);
			switch (kind)
			{
				case InputKind.Axis:
					return new int[] { 0, 65535 };
				case InputKind.Pov:
					return new int[] { -1, 35999 };
				default:
					return new int[] { 0, 0 };
			}
		}

		public static int GetAxisValue(ref InputState state, Inputs input)
		{
			var idx = (int)input;
			if (idx >= 0 && idx < AxisCount)
				return state.Axes[idx];
			return 0;
		}

		public static int GetPovValue(ref InputState state, Inputs input)
		{
			var idx = (int)input - AxisCount;
			if (idx >= 0 && idx < PovCount)
				return state.Povs[idx];
			return -1;
		}

		public static bool GetButtonValue(ref InputState state, Inputs input)
		{
			var idx = (int)input - AxisCount - PovCount;
			if (idx >= 0 && idx < ButtonCount)
				return state.Buttons[idx];
			return false;
		}
	}
}
