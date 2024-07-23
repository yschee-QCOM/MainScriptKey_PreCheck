using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Collections;
using System.Reflection.Emit;

namespace ScriptMap
{
    class Program
    {
        static void Main(string[] args)
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            //user inputs for next 3 lines
            string filePath = @"\\song\pete_taurim\register_settings\SDR753_SoC\V300.220.000.038_RFFE\MAP\SDR753_SoC_PTE_Script_Map_Capabilities_ilna.csv";            
            string userFilePath = @"\\song\pete_taurim\RX\Bench\Sequences\GF_Char\BENCH_GAIN_CHAR\Droop script list.csv";
            string droopFolder = @"\\RFALAB-355\c$\Projects\ACORE_DIG\Documents\TauriM\ADC\DTR_Correction";
            //user inputs end

            filePath = filePath.ToUpper();
            userFilePath = userFilePath.ToUpper();
            droopFolder = droopFolder.ToUpper();
            string projectPath = filePath.Substring(0, filePath.IndexOf("\\REGISTER_SETTINGS"));
            Dictionary<string, string> basePath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<string>> scriptMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<string>> fullScriptMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            HashSet<string> scriptList = new HashSet<string>();
            using (FileStream stream = new FileStream(userFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader reader = new StreamReader(stream))
            {
                string header = reader.ReadLine().ToUpper();
                int targetColumnIndex = Array.IndexOf(header.Split(','), "MAIN_SCRIPT_KEY");
                if (targetColumnIndex != -1)
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        string[] columns = line.Split(',');
                        if (columns.Length > targetColumnIndex)
                        {
                            scriptList.Add(columns[targetColumnIndex].ToUpper());
                        }
                    }
                }
            }

            using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader reader = new StreamReader(stream))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string[] columns = line.Split(',');
                    if (columns.Length > 1)
                    {
                        if (columns[0].Contains("="))
                        {
                            // Remove "=" from the key before saving it to basePath
                            basePath[columns[0].Replace("=", "").ToUpper()] = columns[1].ToUpper();
                        }
                        else
                        {
                            // Check if the first column value is in the user given list
                            if (scriptList.Contains(columns[0].ToUpper()))
                            {
                                List<string> values = new List<string>();
                                for (int i = 1; i < columns.Length; i++)
                                {
                                    if (columns[i].Contains("/"))
                                    {
                                        values.Add(columns[i].ToUpper());
                                    }
                                }
                                scriptMap[columns[0]] = values;
                            }
                        }
                    }
                }
            }

            // Create fullScriptMap with complete file paths
            foreach (KeyValuePair<string, List<string>> item in scriptMap)
            {
                List<string> fullPaths = new List<string>();
                foreach (string value in item.Value)
                {
                    string fullPath;
                    if (value.Contains("SOC/"))
                    {
                        fullPath = Path.Combine(projectPath, basePath["SOC_SCRIPT_BASE_PATH"], value).Replace("/", "\\").Replace("SOC\\", "");
                    }
                    else
                    {
                        fullPath = Path.Combine(projectPath, basePath["RF_SCRIPT_BASE_PATH"], value).Replace("/", "\\");
                    }
                    fullPaths.Add(fullPath);
                }
                fullScriptMap[item.Key] = fullPaths;
            }
/*            //print out fullScriptMap
            foreach (KeyValuePair<string, List<string>> item in fullScriptMap)
            {
                Console.WriteLine($"{item.Key}:");
                foreach (string value in item.Value)
                {
                    Console.WriteLine(value);
                }
                Console.WriteLine();
            }*/

            // Dictionary to hold script keys and their corresponding unfound file paths
            ConcurrentDictionary<string, string> unfoundFiles = new ConcurrentDictionary<string, string>();
            // Dictionary to hold script keys and their corresponding droop headers
            ConcurrentDictionary<string, HashSet<string>> droopHeaders = new ConcurrentDictionary<string, HashSet<string>>();
            // Dictionary to hold script keys and their corresponding script headers
            ConcurrentDictionary<string, HashSet<string>> scriptHeaders = new ConcurrentDictionary<string, HashSet<string>>();

            int totalItems = fullScriptMap.Count;
            int processedItems = 0;

            Console.WriteLine("Checking script components...");
            Parallel.ForEach(fullScriptMap, item =>
            {
                foreach (string value in item.Value)
                {
                    if (File.Exists(value))
                    {   // Read the file and add rows with a ; sign to the list
                        string[] lines = File.ReadAllLines(value);
                        foreach (string line in lines)
                        {
                            if (line.StartsWith(";"))
                            {
                                // Add lines with ; symbol to scriptHeaders
                                scriptHeaders.AddOrUpdate(item.Key, new HashSet<string> { line },
                                    (key, existingVal) => {
                                        existingVal.Add(line);
                                        return existingVal;
                                    });

                                if (line.Contains("DROOP") && line.Contains(".csv"))
                                {
                                    string modifiedLine = line.Replace("[", "").Replace("]", "");
                                    int equalSignIndex = modifiedLine.IndexOf('=');
                                    if (equalSignIndex != -1 && equalSignIndex < modifiedLine.Length - 1)
                                    {
                                        string droopHeader = modifiedLine.Substring(equalSignIndex + 1).TrimEnd(',');
                                        if (!string.IsNullOrWhiteSpace(droopHeader))
                                        {// Split droopHeader by ; and add each part to droopHeaders
                                            string[] parts = droopHeader.Split(';');
                                            foreach (string part in parts)
                                            {
                                                if (!string.IsNullOrWhiteSpace(part))
                                                {
                                                    droopHeaders.AddOrUpdate(item.Key, new HashSet<string> { part },
                                                        (key, existingVal) => {
                                                            existingVal.Add(part);
                                                            return existingVal;
                                                        });
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        unfoundFiles.TryAdd(item.Key, value);
                    }
                }

                // Safely increment the counter
                int newProcessedItems = Interlocked.Increment(ref processedItems);

                // Calculate the progress
                double progress = (double)newProcessedItems / totalItems;

                // Draw the progress bar
                Console.Write("\r[{0}{1}] {2:P2}", new string('#', (int)(progress * 20)), new string(' ', 20 - (int)(progress * 20)), progress);
            });


            List<string> linesToWrite = new List<string>();
            foreach (var pair in scriptHeaders)
            {
                // Remove trailing commas from each header
                HashSet<string> headersWithoutTrailingCommas = new HashSet<string>(
                    pair.Value.Select(header => header.TrimEnd(','))
                );

                string line = pair.Key + "," + string.Join(",", headersWithoutTrailingCommas);
                linesToWrite.Add(line);
            }
            File.WriteAllLines(@"C:\Codes\MainScriptKey_PreCheck\MainScriptKey_PreCheck\scriptHeaders.csv", linesToWrite);
            Console.WriteLine("\nScript Headers can be found here: C:\\Codes\\MainScriptKey_PreCheck\\MainScriptKey_PreCheck\\scriptHeaders.csv");

            // Dictionary to hold script keys and their corresponding droop file paths
            ConcurrentDictionary<string, List<string>> droopFiles = new ConcurrentDictionary<string, List<string>>();
                        
            foreach (var headerSet in droopHeaders)
            {
                foreach (string header in headerSet.Value)
                {
                    string droopFilePath = Path.Combine(droopFolder, header);
                    droopFiles.AddOrUpdate(headerSet.Key, new List<string> { droopFilePath },
                        (key, existingVal) => {
                            existingVal.Add(droopFilePath);
                            return existingVal;
                        });
                }
            }

            Console.WriteLine("\nChecking droop files...");
            totalItems = droopFiles.Count;
            processedItems = 0;
            Parallel.ForEach(droopFiles, fileItem =>
            {
                foreach (string filePath in fileItem.Value)
                {
                    if (!File.Exists(filePath))
                    {
                        unfoundFiles.TryAdd(fileItem.Key, filePath);
                    }
                }
                // Safely increment the counter
                int newProcessedItems = Interlocked.Increment(ref processedItems);

                // Calculate the progress
                double progress = (double)newProcessedItems / totalItems;

                // Draw the progress bar
                Console.Write("\r[{0}{1}] {2:P2}", new string('#', (int)(progress * 20)), new string(' ', 20 - (int)(progress * 20)), progress);

            });

            // Print the list of unfound files or "All files found"
            if (unfoundFiles.Count > 0)
            using (StreamWriter writer = new StreamWriter(@"C:\Codes\MainScriptKey_PreCheck\MainScriptKey_PreCheck\output.csv"))
            {
                    writer.WriteLine("Script_Key,MissingFile"); // CSV header
                    foreach (var entry in unfoundFiles)
                    {
                        string filePaths = string.Join(";", entry.Value); // Join multiple file paths with a semicolon
                        writer.WriteLine($"{entry.Key},{filePaths}"); // CSV row
                    }
                    Console.WriteLine("\nUnfound files can be found here: C:\\Codes\\MainScriptKey_PreCheck\\MainScriptKey_PreCheck\\output.csv");
                }
            else
            {
                Console.WriteLine("All files found");
            }      
            

            stopwatch.Stop();
            Console.WriteLine("\nTime taken: {0} ms", stopwatch.ElapsedMilliseconds);
        }
    }
}
