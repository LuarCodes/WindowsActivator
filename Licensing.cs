using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace WinActivationAssistant
{
    sealed class OsInfo
    {
        public string Caption = "";
        public string EditionId = "";
        public string DisplayVersion = "";
        public string Build = "";
        public int BuildNumber;
        public string Architecture = "";
    }

    sealed class LicenseInfo
    {
        public string Name = "", Description = "", PartialKey = "", ChannelRaw = "";
        public uint Status, GraceMinutes, StatusReason;
        public DateTime? EvaluationEnd;
        public bool KmsHostStored;
        public DateTime ReadAt = DateTime.Now;

        public bool IsActivated => Status == 1;
        public bool IsKmsClient => ChannelRaw.Equals("Volume:GVLK", StringComparison.OrdinalIgnoreCase);

        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case 0: return "Not activated (unlicensed)";
                    case 1: return "Activated";
                    case 2: return "Not activated (initial grace period)";
                    case 3: return "Not activated (additional grace period)";
                    case 4: return "Not activated (non-genuine grace period)";
                    case 5: return "Not activated (notification mode)";
                    case 6: return "Not activated (extended grace period)";
                    default: return "Unknown (" + Status + ")";
                }
            }
        }

        public string ChannelText
        {
            get
            {
                string c = ChannelRaw;
                if (string.IsNullOrEmpty(c))
                {
                    var m = Regex.Match(Description ?? "", @"([A-Z_]+) channel", RegexOptions.IgnoreCase);
                    c = m.Success ? m.Groups[1].Value : "";
                }
                switch (c.ToUpperInvariant())
                {
                    case "RETAIL": return "Retail (incl. digital license)";
                    case "OEM:DM": return "OEM (key embedded in firmware)";
                    case "OEM:NONSLP": case "OEM:COA_NSLP": return "OEM";
                    case "OEM:SLP": case "OEM:COA_SLP": return "OEM (system-locked)";
                    case "VOLUME:GVLK": case "VOLUME_KMSCLIENT": return "Volume license \u2013 KMS client";
                    case "VOLUME:MAK": case "VOLUME_MAK": return "Volume license \u2013 MAK";
                    case "": return "Unknown";
                    default: return c;
                }
            }
        }

        public string ExpiryText
        {
            get
            {
                var parts = new List<string>();
                if (GraceMinutes > 0)
                {
                    var end = ReadAt.AddMinutes(GraceMinutes);
                    int days = (int)Math.Floor(GraceMinutes / 1440.0);
                    string when = end.ToString("d MMM yyyy, HH:mm") + " (" + days + " day" + (days == 1 ? "" : "s") + " left)";
                    if (IsActivated)
                        parts.Add(IsKmsClient ? when + " \u2013 renews from the KMS host" : when);
                    else
                        parts.Add("Grace period ends " + when);
                }
                else if (IsActivated)
                {
                    parts.Add("Never (permanent license)");
                }
                if (EvaluationEnd.HasValue) parts.Add("evaluation ends " + EvaluationEnd.Value.ToString("d MMM yyyy"));
                if (parts.Count == 0) return "\u2014";
                return string.Join("; ", parts);
            }
        }
    }

    sealed class OpResult
    {
        public bool Ok;
        public uint Code;
        public string FailedStep = "";
        public string CustomTitle, CustomMessage;
        public LicenseInfo After;
    }

    static class Licensing
    {
        const string WindowsAppId = "55c92734-d682-4d71-983e-d6ec3f16059f";
        public static readonly Regex KeyPattern = new Regex(@"^[A-Z0-9]{5}(-[A-Z0-9]{5}){4}$", RegexOptions.Compiled);

        // ---------- reading ----------
        public static OsInfo GetOs()
        {
            var o = new OsInfo();
            try
            {
                var view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default;
                using (var hk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (var k = hk.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (k != null)
                    {
                        o.EditionId = k.GetValue("EditionID") as string ?? "";
                        o.DisplayVersion = (k.GetValue("DisplayVersion") as string) ?? (k.GetValue("ReleaseId") as string) ?? "";
                        string build = k.GetValue("CurrentBuild") as string ?? k.GetValue("CurrentBuildNumber") as string ?? "";
                        int.TryParse(build, out o.BuildNumber);
                        object ubr = k.GetValue("UBR");
                        o.Build = ubr != null ? build + "." + ubr : build;
                        o.Caption = k.GetValue("ProductName") as string ?? "";
                    }
                }
            }
            catch { }
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\CIMV2", "SELECT Caption, OSArchitecture FROM Win32_OperatingSystem"))
                using (var col = s.Get())
                    foreach (ManagementObject mo in col)
                        using (mo)
                        {
                            string cap = Prop(mo, "Caption");
                            if (!string.IsNullOrEmpty(cap)) o.Caption = cap;
                            o.Architecture = Prop(mo, "OSArchitecture");
                        }
            }
            catch { }
            o.Caption = (o.Caption ?? "").Replace("Microsoft ", "").Trim();
            // Registry ProductName still says "Windows 10" on Windows 11 – fix it if WMI was unavailable.
            if (o.BuildNumber >= 22000 && o.Caption.StartsWith("Windows 10")) o.Caption = "Windows 11" + o.Caption.Substring(10);
            if (o.BuildNumber == 0) o.BuildNumber = Environment.OSVersion.Version.Build;
            return o;
        }

        public static LicenseInfo GetLicense()
        {
            ManagementObject prod = FindWindowsProduct(null);
            if (prod == null) return null;
            using (prod)
            {
                var li = new LicenseInfo
                {
                    Name = Prop(prod, "Name"),
                    Description = Prop(prod, "Description"),
                    PartialKey = Prop(prod, "PartialProductKey"),
                    ChannelRaw = Prop(prod, "ProductKeyChannel"),
                    Status = PropU(prod, "LicenseStatus"),
                    GraceMinutes = PropU(prod, "GracePeriodRemaining"),
                    StatusReason = PropU(prod, "LicenseStatusReason"),
                    ReadAt = DateTime.Now
                };
                string eval = Prop(prod, "EvaluationEndDate");
                if (!string.IsNullOrEmpty(eval))
                {
                    try
                    {
                        var d = ManagementDateTimeConverter.ToDateTime(eval);
                        if (d.Year > 1601 && d.Year < 9000) li.EvaluationEnd = d;
                    }
                    catch { }
                }
                li.KmsHostStored = !string.IsNullOrEmpty(Prop(prod, "KeyManagementServiceMachine"));
                try
                {
                    using (var svc = GetService())
                        if (!string.IsNullOrEmpty(Prop(svc, "KeyManagementServiceMachine"))) li.KmsHostStored = true;
                }
                catch { }
                return li;
            }
        }

        public static LicenseInfo SafeGetLicense()
        {
            try { return GetLicense(); } catch { return null; }
        }

        // ---------- actions ----------
        public static OpResult ActivateWithKey(string key, IProgress<string> p)
        {
            var r = new OpResult();
            try
            {
                p.Report("Installing your product key\u2026");
                using (var svc = GetService()) Invoke(svc, "InstallProductKey", key);
            }
            catch (Exception ex) { return Fail(r, ex, "installing the product key"); }

            try
            {
                p.Report("Activating with Microsoft\u2026");
                using (var svc = GetService()) TryInvoke(svc, "RefreshLicenseStatus");
                var prod = FindWindowsProduct(key.Substring(key.Length - 5)) ?? FindWindowsProduct(null);
                if (prod == null) throw new COMException("No Windows license found", unchecked((int)0xC004F014));
                using (prod) Invoke(prod, "Activate");
            }
            catch (Exception ex) { Fail(r, ex, "activating Windows"); }

            return Finish(r, p);
        }

        public static OpResult ActivateWithKms(string host, int? port, bool installGvlk, bool eraseAfter, IProgress<string> p)
        {
            var r = new OpResult();
            var os = GetOs();
            var before = SafeGetLicense();

            if (installGvlk && (before == null || !before.IsKmsClient))
            {
                string gvlk = Gvlk.For(os.EditionId, os.BuildNumber);
                if (gvlk == null)
                {
                    r.FailedStep = "checking your edition";
                    r.CustomTitle = "This edition can't use KMS";
                    r.CustomMessage = "KMS activation only works on volume-license editions (Pro, Pro for Workstations, Education, Enterprise, LTSC, Server). "
                        + "Your edition is \"" + os.EditionId + "\". Use a product key instead, or upgrade the edition via the Microsoft Store.";
                    r.After = before;
                    return r;
                }
                try
                {
                    p.Report("Installing Microsoft's KMS client setup key for " + os.EditionId + "\u2026");
                    using (var svc = GetService()) Invoke(svc, "InstallProductKey", gvlk);
                }
                catch (Exception ex) { return Fail(r, ex, "installing the KMS client setup key"); }
            }

            try
            {
                p.Report("Connecting to your KMS host\u2026");
                using (var svc = GetService())
                {
                    Invoke(svc, "SetKeyManagementServiceMachine", host);
                    if (port.HasValue) Invoke(svc, "SetKeyManagementServicePort", (uint)port.Value);
                    else TryInvoke(svc, "ClearKeyManagementServicePort");
                }
                p.Report("Activating with your KMS host\u2026");
                var prod = FindWindowsProduct(null);
                if (prod == null) throw new COMException("No Windows license found", unchecked((int)0xC004F014));
                using (prod) Invoke(prod, "Activate");
            }
            catch (Exception ex) { Fail(r, ex, "activating with the KMS host"); }
            finally
            {
                if (eraseAfter)
                {
                    p.Report("Erasing the KMS address from Windows\u2026");
                    EraseStoredKmsHost();
                }
            }
            return Finish(r, p);
        }

        /// Removes the KMS host name/port that Windows keeps in its licensing settings.
        public static bool EraseStoredKmsHost()
        {
            bool ok = true;
            try
            {
                using (var svc = GetService())
                {
                    ok &= TryInvoke(svc, "ClearKeyManagementServiceMachine");
                    ok &= TryInvoke(svc, "ClearKeyManagementServicePort");
                }
            }
            catch { ok = false; }
            try
            {
                var prod = FindWindowsProduct(null);
                if (prod != null)
                    using (prod)
                    {
                        TryInvoke(prod, "ClearKeyManagementServiceMachine");
                        TryInvoke(prod, "ClearKeyManagementServicePort");
                    }
            }
            catch { }
            return ok;
        }

        static OpResult Finish(OpResult r, IProgress<string> p)
        {
            p.Report("Re-reading the license status\u2026");
            try { using (var svc = GetService()) TryInvoke(svc, "RefreshLicenseStatus"); } catch { }
            r.After = SafeGetLicense();
            r.Ok = r.After != null && r.After.IsActivated && (r.Code == 0 || (r.Code & 0x80000000) == 0);
            if (!r.Ok && r.Code == 0)
            {
                r.Code = r.After != null ? r.After.StatusReason : 0xC004F014;
                if (string.IsNullOrEmpty(r.FailedStep)) r.FailedStep = "verifying the license";
            }
            if (r.Ok) r.Code = 0;
            return r;
        }

        static OpResult Fail(OpResult r, Exception ex, string step)
        {
            r.Code = HResultOf(ex);
            r.FailedStep = step;
            r.After = SafeGetLicense();
            return r;
        }

        // ---------- WMI helpers ----------
        static ManagementObject GetService()
        {
            using (var s = new ManagementObjectSearcher(@"root\CIMV2", "SELECT * FROM SoftwareLicensingService"))
            using (var col = s.Get())
                foreach (ManagementObject mo in col) return mo;
            throw new COMException("Software Licensing Service not available", unchecked((int)0x80070422));
        }

        static ManagementObject FindWindowsProduct(string partialKey)
        {
            string q = "SELECT * FROM SoftwareLicensingProduct WHERE ApplicationID='" + WindowsAppId + "' AND PartialProductKey IS NOT NULL";
            ManagementObject best = null;
            using (var s = new ManagementObjectSearcher(@"root\CIMV2", q))
            using (var col = s.Get())
            {
                foreach (ManagementObject mo in col)
                {
                    bool addon = Prop(mo, "LicenseIsAddon").Equals("True", StringComparison.OrdinalIgnoreCase);
                    string pk = Prop(mo, "PartialProductKey");
                    bool match = partialKey == null ? !addon : pk.Equals(partialKey, StringComparison.OrdinalIgnoreCase);
                    if (match && best == null) { best = mo; continue; }
                    mo.Dispose();
                }
            }
            return best;
        }

        static void Invoke(ManagementObject mo, string method, params object[] args)
        {
            object ret = mo.InvokeMethod(method, args == null || args.Length == 0 ? null : args);
            if (ret != null)
            {
                uint code = Convert.ToUInt32(ret);
                if (code != 0) throw new COMException(method + " failed", unchecked((int)code));
            }
        }

        static bool TryInvoke(ManagementObject mo, string method)
        {
            try { Invoke(mo, method); return true; } catch { return false; }
        }

        public static uint HResultOf(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                if (e is COMException c) return unchecked((uint)c.ErrorCode);
                if (e is ManagementException m) return unchecked((uint)(int)m.ErrorCode);
                if (e is UnauthorizedAccessException) return 0x80070005;
            }
            return unchecked((uint)ex.HResult);
        }

        static string Prop(ManagementBaseObject mo, string name)
        {
            try { return mo[name]?.ToString() ?? ""; } catch { return ""; }
        }

        static uint PropU(ManagementBaseObject mo, string name)
        {
            try { var v = mo[name]; return v == null ? 0 : Convert.ToUInt32(v); } catch { return 0; }
        }
    }

    /// Microsoft's public KMS client setup keys (GVLKs), from
    /// https://learn.microsoft.com/windows-server/get-started/kms-client-activation-keys
    static class Gvlk
    {
        public static string For(string edition, int build)
        {
            switch (edition ?? "")
            {
                case "Professional": return "W269N-WFGWX-YVC9B-4J6C9-T83GX";
                case "ProfessionalN": return "MH37W-N47XK-V7XM9-C7227-GCQG9";
                case "ProfessionalWorkstation": return "NRG8B-VKK3Q-CXVCJ-9G2XF-6Q84J";
                case "ProfessionalWorkstationN": return "9FNHH-K3HBT-3W4TD-6383H-6XYWF";
                case "ProfessionalEducation": return "6TP4R-GNPTD-KYYHQ-7B7DP-J447Y";
                case "ProfessionalEducationN": return "YVWGF-BXNMC-HTQYQ-CPQ99-66QFC";
                case "Education": return "NW6C2-QMPVW-D7KKK-3GKT6-VCFB2";
                case "EducationN": return "2WH4N-8QGBV-H22JP-CT43Q-MDWWJ";
                case "Enterprise": return "NPPR9-FWDCX-D2C8J-H872K-2YT43";
                case "EnterpriseN": return "DPH2V-TTNVB-4X9Q3-TJR4H-KHJW4";
                case "EnterpriseG": return "YYVX9-NTFWV-6MDM3-9PT4T-4M68B";
                case "EnterpriseGN": return "44RPN-FTY23-9VTTB-MP9BX-T84FV";
                case "EnterpriseS":
                    return build >= 17763 ? "M7XTQ-FN8P6-TTKYV-9D4CC-J462D" : build >= 14393 ? "DCPHK-NFMTC-H88MJ-PFHPY-QJ4BJ" : "WNMTR-4C88C-JK8YV-HQ7T2-76DF9";
                case "EnterpriseSN":
                    return build >= 17763 ? "92NFX-8DJQP-P6BBQ-THF9C-7CG2H" : build >= 14393 ? "QFFDN-GRT3P-VKWWX-X7T3R-8B639" : "2F77B-TNFGY-69QQF-B8YKP-D69TJ";
                case "IoTEnterpriseS":
                    return build >= 19044 ? "KBN8V-HFGQ4-MGXVD-347P6-PDQGT" : null;
                case "ServerStandard":
                    return build >= 26100 ? "TVRH6-WHNXV-R9WG3-9XRFY-MY832" : build >= 20348 ? "VDYBN-27WPP-V4HQT-9VMD4-VMK7H"
                         : build >= 17763 ? "N69G4-B89J2-4G8F4-WWYCC-J464C" : build >= 14393 ? "WC2BQ-8NRM3-FDDYY-2BFGV-KHKQY" : null;
                case "ServerDatacenter":
                    return build >= 26100 ? "D764K-2NDRG-47T6Q-P8T8W-YP6DF" : build >= 20348 ? "WX4NM-KYWYW-QJJR4-XV3QB-6VM33"
                         : build >= 17763 ? "WMDGN-G9PQG-XVVXX-R3X43-63DFG" : build >= 14393 ? "CB7KF-BWN84-R7R2Y-793K2-8XDDG" : null;
                default: return null; // Home / Core / Single Language etc. cannot use KMS
            }
        }
    }

    static class ErrorCodes
    {
        static readonly Dictionary<uint, string> Map = new Dictionary<uint, string>
        {
            [0xC004F050] = "The product key is invalid. Check it for typos and make sure it's a Windows key (not Office) for this version of Windows.",
            [0xC004F069] = "This key doesn't match your edition of Windows (for example a Pro key on Home). To switch editions use Settings \u203a Activation \u203a Change product key.",
            [0xC004E016] = "Windows refused the key: it isn't valid for this edition or installation type.",
            [0xC004F034] = "The key is invalid or can't be used with this version of Windows.",
            [0xC004C003] = "Microsoft's activation server blocked this key. It may be counterfeit, refunded or already reported \u2013 contact the seller or Microsoft.",
            [0xC004C001] = "Microsoft's activation server says the product key is invalid.",
            [0xC004C008] = "This key has already been used on more PCs than its license allows.",
            [0xC004C020] = "This volume (MAK) key has reached its activation limit. Ask your licensing admin for more activations.",
            [0xC004C021] = "This volume (MAK) key has exceeded its activation limit.",
            [0xC004C017] = "This key is blocked for use in your country or region.",
            [0xC004C060] = "This product key is no longer valid.",
            [0xC004C4AE] = "Windows found modified system files during genuine validation. Run 'sfc /scannow' and try again.",
            [0xC004F061] = "This key can only be used to upgrade an existing activated Windows, not on a clean install.",
            [0xC004F063] = "The PC's firmware (BIOS) is missing the OEM license this key requires.",
            [0xC004F035] = "Volume licensing on this PC requires a qualifying underlying Windows license. Use a MAK or retail key instead.",
            [0xC004F00F] = "The hardware has changed too much since the last activation. Re-enter your key or use the Activation troubleshooter in Settings.",
            [0xC004F009] = "The grace period has expired. Enter a valid product key to activate.",
            [0xC004F014] = "No product key is installed. Enter a product key or your company's KMS address.",
            [0xC004F012] = "A licensing value wasn't found \u2013 the licensing store may be damaged. Restart, run 'sfc /scannow', then try again.",
            [0xC004E003] = "License evaluation failed because a system file or license was changed. Run 'sfc /scannow' and try again.",
            [0xC004F025] = "Access denied \u2013 the app must run as administrator.",
            [0x80070005] = "Access denied \u2013 the app must run as administrator.",
            [0xC004F074] = "No KMS host could be reached. Check the address and port (default 1688), that you're on the office network or VPN, and that TCP 1688 isn't blocked by a firewall.",
            [0xC004F038] = "The KMS host answered, but it doesn't have enough clients yet (it needs 25 PCs or 5 servers). Contact your IT team.",
            [0xC004F039] = "The KMS host answered, but KMS activation isn't enabled on it. Contact your IT team.",
            [0xC004F041] = "The KMS host itself isn't activated with Microsoft. Contact your IT team.",
            [0xC004F042] = "The KMS host can't activate this product \u2013 its host key doesn't cover this version of Windows. Contact your IT team.",
            [0xC004F015] = "The KMS host's key doesn't cover this edition/version (common when activating Windows 11 against an older host). Contact your IT team.",
            [0xC004F06C] = "The PC's clock is too far off from the KMS host's clock. Fix the date, time and time zone, then try again.",
            [0xC004F056] = "KMS activation failed. Check the KMS address, the network connection and that this PC has a volume-license key.",
            [0x8007232B] = "DNS name does not exist: Windows couldn't find a KMS host automatically. Enter your KMS address explicitly, or use a product key.",
            [0x8007232A] = "A DNS server failure occurred while looking up the KMS host. Check your network and DNS settings.",
            [0x8007251D] = "No DNS records were found for the KMS host. Check the address spelling.",
            [0x8007267C] = "No DNS server is configured on this PC. Check your network settings.",
            [0x800706BA] = "The KMS host refused or blocked the connection (RPC server unavailable). Check the address, port and firewall.",
            [0x80072EE2] = "The connection timed out. Check your internet/network connection and proxy settings.",
            [0x80072EE7] = "The server name couldn't be resolved. Check your internet connection or the spelling of the address.",
            [0x80072EFD] = "Couldn't connect to the activation server. Check your internet connection and firewall.",
            [0x80072F8F] = "A secure connection couldn't be made \u2013 usually because the PC's date/time is wrong. Fix the clock and retry.",
            [0x8007007B] = "A KMS client (volume) key is installed but no KMS host was found. Enter your company's KMS address, or install your own product key.",
            [0x80070490] = "Licensing data is incomplete. Re-install the product key and try again.",
            [0x80070422] = "The Software Protection service (sppsvc) is disabled. Set it back to Automatic (Delayed Start) and try again.",
            [0x803F7001] = "No valid license was found for this PC (no digital license and no working key). Enter a product key or buy a license in the Microsoft Store.",
            [0xC004F213] = "Windows found no product key or digital license linked to this hardware. If you changed hardware, run the Activation troubleshooter in Settings.",
            [0x80004005] = "An unspecified error occurred. Restart the PC and try again; if it repeats, use the Activation troubleshooter in Settings.",
            [0x4004F040] = "Activated, but Windows recommends verifying your product use rights.",
        };

        public static string Explain(uint code)
        {
            if (code == 0) return "";
            if (Map.TryGetValue(code, out var s)) return s;
            return "Windows returned an error that this app doesn't have a description for. Search Microsoft Support for the code above, or run the Activation troubleshooter in Settings.";
        }

        public static string Format(uint code) => code == 0 ? "\u2014" : "0x" + code.ToString("X8");
    }
}
