using Microsoft.Win32;
using System;
using System.Diagnostics;

namespace XJoy
{
	public static class HidGuardianHelper
	{
		const string HidGuardianRegistry = @"SYSTEM\\CurrentControlSet\\Services\\HidGuardian";
		const string ParametersRegistry = HidGuardianRegistry + "\\Parameters";
		const string WhitelistRegistry = ParametersRegistry + "\\Whitelist";

		public static bool TryInsertCurrentProcessToWhiteList()
		{
			try
			{
				int id = Process.GetCurrentProcess().Id;
				return InsertToWhiteList(id);
			}
			catch
			{
				return false;
			}
		}

		public static bool TryRemoveCurrentProcessFromWhiteList()
		{
			try
			{
				int id = Process.GetCurrentProcess().Id;
				return RemoveFromWhiteList(id);
			}
			catch
			{
				return false;
			}
		}

		static bool InsertToWhiteList(int processId)
		{
			using (var subKey = Registry.LocalMachine.OpenSubKey(WhitelistRegistry, true))
			{
				if (subKey == null)
					return false;
				using (var key = subKey.CreateSubKey(processId.ToString()))
				{
					return key != null;
				}
			}
		}

		static bool RemoveFromWhiteList(int processId)
		{
			using (var subKey = Registry.LocalMachine.OpenSubKey(WhitelistRegistry, true))
			{
				if (subKey == null)
					return false;
				subKey.DeleteSubKey(processId.ToString(), false);
				return true;
			}
		}
	}
}
