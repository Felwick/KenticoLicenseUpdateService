using CMS.Core;
using CMS.EventLog;
using CMS.LicenseProvider;
using CMS.Scheduler;
using KenticoLicenseUpdateService.com.kentico.service;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KenticoLicenseUpdateService
{
    public class LicenseUpdater : ITask
    {
        Stopwatch stopWatch = new Stopwatch();
        int count = 0;

        public string Execute(TaskInfo task)
        {
            IEventLogService eventLog = Service.Resolve<IEventLogService>(); ;

            eventLog.LogInformation(nameof(LicenseUpdater), "I", $"{nameof(LicenseUpdater)} has started running");
            stopWatch.Start();
            int retries = 3;
            int numberOfKeys = 0;
            string userName = "";
            string licenseKeySerial = "";
            bool deleteOldKeys = false;
            //Default run next year because of one year key expiration 
            DateTime nextRunDate = DateTime.Now.AddYears(1);
            List<LicenseKeyInfo> instanceKeys = LicenseKeyInfoProvider.GetLicenseKeys().ToList();

            if (task.TaskData != "")
            {
                string[] parameters = task.TaskData.Split('\n');

                userName = parameters[0];
                licenseKeySerial = parameters[1];
                if (!int.TryParse(parameters[2], out numberOfKeys))
                {
                    numberOfKeys = instanceKeys.Count;
                }
                if(String.Equals(parameters[3],"true",StringComparison.OrdinalIgnoreCase))
                {
                    deleteOldKeys = true;
                }
            }
            //optional hardcoded fallback values
            else
            {
                userName = "";
                licenseKeySerial = "";
                numberOfKeys = instanceKeys.Count;

            }

            List<string> generatedKeys = new List<string>();

            string errorMessage = null;

            for (int i = 0; i < numberOfKeys; i++)
            {
                LicenseKeyInfo key = instanceKeys[i];
                if (errorMessage == null && retries != 0)
                {
                    var licenseKey = GetLicenseKey(licenseKeySerial, key.Domain, userName, out errorMessage);
                    //Force sleep to avoid hitting request rate limit on service side
                    Thread.Sleep(800);

                    //error check, reset and retry
                    if (errorMessage != null)
                    {
                        eventLog.LogInformation(nameof(LicenseUpdater), "I", $"Licence service error: {errorMessage}. Retry attempts left: {retries}");
                        errorMessage = null;
                        //iterator reset for retry
                        i--;
                        retries--;
                        continue;
                    }

                    generatedKeys.Add(licenseKey);
                    LicenseKeyInfoProvider.DeleteLicenseKeyInfo(key);
                }

                if (retries == 0)
                {
                    eventLog.LogEvent(EventTypeEnum.Error, "E", $"Licence service error: {errorMessage}. Retries exhausted, attempts left: {retries}. Event time: {DateTime.Now}");
                    return $"Licence service error: {errorMessage}. Retries exhausted, attempts left: {retries}. Event time: {DateTime.Now}";
                }

            }
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

            task.TaskNextRunTime = nextRunDate;
            stopWatch.Stop();
            eventLog.LogInformation(nameof(LicenseUpdater), "I", $"Licence key service run finished. Runtime: {stopWatch.Elapsed}, generated keys: {generatedKeys.Count}");

            return $"Licence key service run finished. Runtime: {stopWatch.Elapsed}, generated keys: {generatedKeys.Count}";
        }

        private static string GetLicenseKey(string sn, string domain, string userName, out string errorMessage)
        {
            RSACryptoServiceProvider rcp = new RSACryptoServiceProvider();
            rcp.FromXmlString("<RSAKeyValue><Modulus>4yUuUVYKw0lQDTMONy356ufkOgSUjeGdP168JdNAQbGnaqSuXek/qe0HztzUteY4oWR73CimGNshL9viCcmc/AZhWoLUdiML1rii6Rup7KRXY4azti65cmgADeFXkO3Cl2dmyQaYX6IN+VHTTjp1B3SSdqv2dbz0VFwjZuVG/1DK9avlnQkS04W5UAGNR3ZDfqBJaw7Fou/7X2psH6S0xXVV+qy64qgJcfe3OkyH+zqUCEf6hOJwBeGNXc3NWw629UatPg7cgvLvj/JSDfuNmUKrVkC40GaLXkAuPUZiyledyEb3a/G2D8YjG48Xk4qxz1vtBd+EsIaiNez2iVx5Dw==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>");

            CMSLicenseService service = new CMSLicenseService();

            // Encrypted serial, domain and username
            string data = Convert.ToBase64String(rcp.Encrypt(ASCIIEncoding.ASCII.GetBytes(sn + "|" + domain + "|" + userName), false));

            // If version is not set directly, key will be the same version as the serial number
            int? version = null;

            // Different types of keys - Main will use up a slot of the license, other types can be used only for unlimited licenses
            LicenseKeyTypeEnum keyType = LicenseKeyTypeEnum.Main;

            return service.GetKeyGeneral(data, version, keyType, out errorMessage);
        }
    }
}
