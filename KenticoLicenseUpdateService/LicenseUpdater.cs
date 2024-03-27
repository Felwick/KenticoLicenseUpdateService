using CMS.Core;
using CMS.EventLog;
using CMS.LicenseProvider;
using CMS.Scheduler;
using CMS.SiteProvider;
using KenticoLicenseUpdateService.com.kentico.service;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace KenticoLicenseUpdateService
{
    public class LicenseUpdater : ITask
    {
        Stopwatch stopWatch = new Stopwatch();
        int count = 0;
        int retries = 3;
        int numberOfKeys = 0;
        int desiredVersion = 0;
        bool deleteOldKeys = false;
        string userName = "";
        string licenseKeySerial = "";

        public int Retries { get => retries; set => retries = value; }
        public int NumberOfKeys { get => numberOfKeys; set => numberOfKeys = value; }
        public int DesiredVersion { get => desiredVersion; set => desiredVersion = value; }
        public bool DeleteOldKeys { get => deleteOldKeys; set => deleteOldKeys = value; }
        public string UserName { get => userName; set => userName = value; }
        public string LicenseKeySerial { get => licenseKeySerial; set => licenseKeySerial = value; }

        public string Execute(TaskInfo task)
        {
            IEventLogService eventLog = Service.Resolve<IEventLogService>();
            
            eventLog.LogEvent( EventTypeEnum.Information,nameof(LicenseUpdater), "I", $"{nameof(LicenseUpdater)} has started running");
            stopWatch.Start();
            stopWatch.Start();
            string resultMessage;

            //Default run next year because of one year key expiration 
            DateTime nextRunDate = DateTime.Now.AddYears(1);
            List<LicenseKeyInfo> instanceKeys = LicenseKeyInfoProvider.GetLicenseKeys().ToList();

            ParseParametersInput(task, instanceKeys);

            List<string> generatedKeys = new List<string>();

            resultMessage = GenerateNewKeys(generatedKeys, instanceKeys, eventLog);

            if (generatedKeys.Any())
            {
                eventLog.LogEvent(EventTypeEnum.Information,nameof(LicenseUpdater), "I", $"Licence key service run finished. Runtime: {stopWatch.Elapsed}, no generated keys");
                return $"Task finished running with no generated keys";
            }
            ProcessAndInserNewKeys(generatedKeys, nextRunDate);

            task.TaskNextRunTime = nextRunDate;
            stopWatch.Stop();
            eventLog.LogEvent(EventTypeEnum.Information,nameof(LicenseUpdater), "I", $"Licence key service run finished. Runtime: {stopWatch.Elapsed}, generated keys: {generatedKeys.Count}");

            return $"Licence key service run finished. Runtime: {stopWatch.Elapsed} {resultMessage}";
        }

        private void ProcessAndInserNewKeys(List<string> generatedKeys, DateTime nextRunDate)
        {
            foreach (var key in generatedKeys)
            {

                string[] splitString = key.Split(Environment.NewLine.ToCharArray());
                if (splitString.Any())
                {
                    int index = splitString[3].IndexOf("EXPIRATION:", StringComparison.Ordinal) + 11;
                    string timeString = splitString[3].Substring(index, 8).Trim();
                    DateTime expiryDateTime = new DateTime(Convert.ToInt32(timeString.Substring(0, 4)), Convert.ToInt32(timeString.Substring(4, 2)), Convert.ToInt32(timeString.Substring(6, 2)), 0, 0, 0);
                    if (DateTime.Compare(expiryDateTime, nextRunDate) < 0)
                    {
                        nextRunDate = expiryDateTime;
                    }
                    LicenseKeyInfo licenseKeyInfo = new LicenseKeyInfo();
                    licenseKeyInfo.LoadLicense(key, splitString[0]);
                    licenseKeyInfo.Insert();
                }

            }
        }

        private string GenerateNewKeys(List<string> generatedKeys, List<LicenseKeyInfo> instanceKeys, IEventLogService eventLog)
        {
            string errorMessage = null;

            for (int i = 0; i < NumberOfKeys; i++)
            {
                LicenseKeyInfo key = instanceKeys[i];
                if (errorMessage == null && Retries != 0)
                {
                    var licenseKey = GetLicenseKey(LicenseKeySerial, key.Domain, DesiredVersion, UserName, out errorMessage);

                    //Force sleep to avoid hitting request rate limit on service side
                    Thread.Sleep(800);

                    //error check, reset and retry
                    if (errorMessage != null)
                    {
                        eventLog.LogError(nameof(LicenseUpdater), "I", $"Licence service error: {errorMessage}. Retry attempts left: {Retries}");
                        errorMessage = null;

                        //iterator reset
                        i--;
                        Retries--;
                        continue;
                    }

                    generatedKeys.Add(licenseKey);

                    if (DeleteOldKeys == true)
                    {
                        LicenseKeyInfoProvider.DeleteLicenseKeyInfo(key);
                    }

                }

                if (Retries == 0)
                {
                    eventLog.LogError(nameof(LicenseUpdater), "E", $"Licence service error: {errorMessage}. Retries exhausted, attempts left: {Retries}. Event time: {DateTime.Now}");
                    return $"Licence service error: {errorMessage}. Retries exhausted, attempts left: {Retries}. Event time: {DateTime.Now}";
                }

            }
            return $"{generatedKeys.Count} license keys were generated with setting DeleteOldKeys set to {DeleteOldKeys.ToString()}";
        }

        private static string GetLicenseKey(string sn, string domain, int desiredVersion, string userName, out string errorMessage)
        {
            RSACryptoServiceProvider rcp = new RSACryptoServiceProvider();
            rcp.FromXmlString("<RSAKeyValue><Modulus>4yUuUVYKw0lQDTMONy356ufkOgSUjeGdP168JdNAQbGnaqSuXek/qe0HztzUteY4oWR73CimGNshL9viCcmc/AZhWoLUdiML1rii6Rup7KRXY4azti65cmgADeFXkO3Cl2dmyQaYX6IN+VHTTjp1B3SSdqv2dbz0VFwjZuVG/1DK9avlnQkS04W5UAGNR3ZDfqBJaw7Fou/7X2psH6S0xXVV+qy64qgJcfe3OkyH+zqUCEf6hOJwBeGNXc3NWw629UatPg7cgvLvj/JSDfuNmUKrVkC40GaLXkAuPUZiyledyEb3a/G2D8YjG48Xk4qxz1vtBd+EsIaiNez2iVx5Dw==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>");

            CMSLicenseService service = new CMSLicenseService();

            // Encrypted serial, domain and username
            string data = Convert.ToBase64String(rcp.Encrypt(ASCIIEncoding.ASCII.GetBytes(sn + "|" + domain + "|" + userName), false));

            // If version is not set directly, key will be the same version as the serial number using this as a default fallback if version is not supplied specifically
            int? version = null;
            if (desiredVersion != 0)
            {
                version = desiredVersion;
            }

            // Different types of keys - Main will use up a slot of the license, other types can be used only for unlimited licenses
            LicenseKeyTypeEnum keyType = LicenseKeyTypeEnum.Main;

            return service.GetKeyGeneral(data, version, keyType, out errorMessage);
        }

        private void ParseParametersInput(TaskInfo task, List<LicenseKeyInfo> instanceKeys)
        {
            if (task.TaskData != "")
            {
                string[] parameters = task.TaskData.Split('\n');

                this.UserName = parameters[0];
                LicenseKeySerial = parameters[1];
                int desiredVersion;

                if (!int.TryParse(parameters[2], out desiredVersion))
                {
                    DesiredVersion = 0;
                }

                DesiredVersion = desiredVersion;

                int numberOfKeys;

                if (!int.TryParse(parameters[3], out numberOfKeys))
                {
                    NumberOfKeys = instanceKeys.Count;
                }
                NumberOfKeys = numberOfKeys;

                if (String.Equals(parameters[4], "true", StringComparison.OrdinalIgnoreCase))
                {
                    DeleteOldKeys = true;
                }

            }
            else
            {
                UserName = "";
                LicenseKeySerial = "";
                NumberOfKeys = instanceKeys.Count;

            }
        }
    }
}