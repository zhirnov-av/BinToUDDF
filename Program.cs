using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Xml;
using System.Globalization;

namespace BinToUDDF
{
    class Program
    {

        private static XmlDocument uddfDoc = new XmlDocument();
        private static XmlNode xGasdefenition;

        private static Dictionary<String, String> gases = new Dictionary<string, string>();

        internal struct DiveInfo
        {
            public UInt32 DiveNumber;
            public UInt32 DateTime32;
            public Single AirP;
            public UInt32 DivePositionBeg;
            public UInt32 DivePositionEnd;
            public UInt32 WarningFlags;

            public Single[] piN2;
            public Single[] piHe;

            public Single CNS;

            public int DiveDuration;
            public UInt16 MaxDepth;
            public UInt16 AvgDepth;
            public int MinTemp;

            public DateTime DiveDateTime;
            public int RepetitionGroup;
        }

        static String getGas(byte o2, byte he)
        {
            string id = o2.ToString() + "/" + he.ToString();
            string gas;

            if (!gases.TryGetValue(id, out gas))
            {
                switch (id)
                {
                    case "21/0":
                        gases.Add("21/0", "Air");
                        gas = "Air";
                        break;
                    case "99/0":
                        gases.Add("99/0", "Oxy");
                        gas = "Oxy";
                        break;
                    case "100/0":
                        gases.Add("99/0", "Oxygen");
                        gas = "Oxygen";
                        break;
                    default:
                        gases.Add(id, id);
                        gas = id;
                        break;
                }

                XmlNode mix = uddfDoc.CreateElement("mix");

                XmlAttribute attr = uddfDoc.CreateAttribute("id");
                attr.Value = gas;
                mix.Attributes.Append(attr);
                xGasdefenition.AppendChild(mix);

                XmlNode tmp = uddfDoc.CreateElement("name");
                tmp.InnerText = gas;
                mix.AppendChild(tmp);

                tmp = uddfDoc.CreateElement("o2");
                tmp.InnerText = String.Format(CultureInfo.InvariantCulture, "{0:F2}", (double)o2 / 100.0);
                mix.AppendChild(tmp);

                tmp = uddfDoc.CreateElement("n2");
                tmp.InnerText = String.Format(CultureInfo.InvariantCulture, "{0:F2}", (double)(100 - o2 - he) / 100.0);
                mix.AppendChild(tmp);

                tmp = uddfDoc.CreateElement("he");
                tmp.InnerText = String.Format(CultureInfo.InvariantCulture, "{0:F2}", (double)(he) / 100.0);
                mix.AppendChild(tmp);
            }

            return gas;
        }

        static DateTime ConvertDateTime(UInt32 seconds)
        {
            int mi, ss, hh;

            int minutes = (int)seconds / 60;
            double tmp = (double)(seconds / 60.0) - (double)minutes;
            ss = (int)Math.Round(tmp * 60);

            int hours = minutes / 60;
            tmp = (double)(minutes / 60.0) - (double)hours;
            mi = (int)Math.Round(tmp * 60);

            int days = hours / 24;
            tmp = (double)(hours / 24.0) - (double)days;
            hh = (int)Math.Round(tmp * 24);

            DateTime dt = new DateTime(2010, 01, 01);

            dt = dt.AddDays((double)days);
            dt = dt.AddSeconds((double)(seconds - days * 86400));

            int LeapYears = 0; 

            for (int i = dt.Year; i >= 2010; i--)
            {
                if (DateTime.IsLeapYear(i)) LeapYears++;
            }

            dt = new DateTime(2009, 12, 31);

            dt = dt.AddDays((double)days + LeapYears);
            dt = dt.AddSeconds((double)(seconds - days * 86400));
            
            return dt;
        }

        static Dictionary<string, string> ParseCommandLine(string[] args)
        {
            Dictionary<string, string> arguments = new Dictionary<string, string>();

            for (int i = 0; i < args.GetLength(0); i++)
            {
                String word1 = args[i];
                String word2 = "";
                if (i + 1 < args.GetLength(0) && args[i + 1] == "=")
                {
                    if (i + 2 < args.GetLength(0))
                    {
                        word2 = args[i + 2];
                        i += 2;
                    }
                }
                else
                {
                    word2 = word1;
                    word1 = word1.Substring(0, word1.IndexOf('='));
                    word2 = word2.Substring(word2.IndexOf('=') + 1);
                }
                if (word1 != "" && word2 != "") arguments.Add(word1.ToLower(), word2.ToLower());
            }

            return arguments;
        }

        internal static XmlNode AddNodeWithInnerText(XmlNode parent, String name, String innerText)
        {
            XmlNode tmp = uddfDoc.CreateElement(name);
            tmp.InnerText = innerText;
            if (parent != null) parent.AppendChild(tmp);
            return tmp;
        }

        internal static XmlNode AddNodeWithAttribute(XmlNode parent, String name, String attrName, String attrValue)
        {

            XmlNode tmp = uddfDoc.CreateElement(name);
            XmlAttribute attr = uddfDoc.CreateAttribute(attrName); 
            attr.Value = attrValue; 
            tmp.Attributes.Append(attr); 
            if (parent != null) parent.AppendChild(tmp);
            return tmp;

        }

        internal static XmlNode AddNode(XmlNode parent, String name)
        {
            XmlNode tmp = uddfDoc.CreateElement(name);
            if (parent != null) parent.AppendChild(tmp);
            return tmp;
        }

        static void Main(string[] args)
        {
            int currTime;

            byte helium, oxygen;
            
            byte bt;

            bool bFlgExit = false;
            bool bWPOpen;

            Dictionary<string, string> arguments = ParseCommandLine(args);
            
            if (arguments.Count < 1)
            {
                Console.WriteLine("ERROR: Неверное количество аргументов командной строки!");
                return;
            }
            string binFile;
            string uddfFile;
            if (!arguments.TryGetValue("binfile", out binFile))
            {
                Console.WriteLine("ERROR: Не определен binfile!");
                return;
            }
            if (!arguments.TryGetValue("uddffile", out uddfFile))
            {
                uddfFile = binFile.Replace(".bin", ".uddf");
            }

            List<DiveInfo> dives = new List<DiveInfo>();
            DiveInfo dive = new DiveInfo();
            BinaryReader file;
            try
            {
                 file = new BinaryReader(File.Open(binFile, FileMode.Open));
            }
            catch (Exception e) 
            {
                Console.WriteLine(String.Format("Файл {0} не найден или занят.", binFile));
                return;
            }

            for (int i = 0; i < 121; i++)
            {
                dive.DiveNumber = file.ReadUInt32();
                dive.DateTime32 = file.ReadUInt32();
                dive.AirP = file.ReadSingle() * 100000;

                dive.DiveDateTime = ConvertDateTime(dive.DateTime32);

                dive.DivePositionBeg = file.ReadUInt32();
                dive.DivePositionEnd = file.ReadUInt32();
                dive.WarningFlags = file.ReadUInt32();

                dive.piN2 = new Single[16];
                dive.piHe = new Single[16];

                byte[] tmpArray = new byte[64];

                tmpArray = file.ReadBytes(64);
                for (int j = 0; j < 16; j++) dive.piN2[j] = BitConverter.ToSingle(tmpArray, j * 4);
                tmpArray = file.ReadBytes(64);
                for (int j = 0; j < 16; j++) dive.piHe[j] = BitConverter.ToSingle(tmpArray, j * 4);

                dive.CNS = file.ReadSingle();
                dive.DiveDuration = file.ReadInt32();

                dive.MaxDepth = file.ReadUInt16();
                dive.AvgDepth = file.ReadUInt16();
                dive.MinTemp = file.ReadInt32();

                if (dive.DiveDuration > 0)
                    dives.Add(dive);
                else
                    break;
            }

            XmlTextWriter logbook = new XmlTextWriter(uddfFile, Encoding.ASCII);
            logbook.WriteStartElement("uddf");
            logbook.WriteEndElement();
            logbook.Close();

            uddfDoc.Load(uddfFile);

            XmlNode uddf = uddfDoc.DocumentElement;

            XmlAttribute attr = uddfDoc.CreateAttribute("version"); // создаём атрибут
            attr.Value = "3.0.0"; // устанавливаем значение атрибута
            uddf.Attributes.Append(attr); // добавляем атрибут

            XmlNode xGenerator = AddNode(uddf, "generator");

            XmlNode xTmp = AddNodeWithInnerText(xGenerator, "name", "AV1c");

            xTmp = AddNodeWithInnerText(xGenerator, "type", "divecomputer");

            xTmp = AddNode(xGenerator, "manufacturer");

            AddNodeWithInnerText(xTmp, "name", "AV Underwater Technologies");

            xGasdefenition = AddNode(uddf, "gasdefinitions");

            XmlNode profiledata = AddNode(uddf, "profiledata");


            int group = 1;
            dive = dives[0];

            DateTime prevDate = dive.DiveDateTime;

            for (int i = 0; i < dives.Count; i++ )
            {
                dive = dives[i];

                if (i == 0)
                {
                    dive.RepetitionGroup = group;
                }
                else
                {
                    if ((dive.DiveDateTime.Subtract(prevDate)).Days > 2) group++;
                    dive.RepetitionGroup = group;
                    prevDate = dive.DiveDateTime;
                }

                dives[i] = dive;

            }

            dive = dives[0];

            XmlNode repetitiongroup = AddNodeWithAttribute(profiledata, "repetitiongroup", "id", "1");
            group = 1;

            for (int i = 0; i < dives.Count; i++)
            {

                dive = dives[i];

                if (dive.RepetitionGroup != group)
                {
                    repetitiongroup = AddNodeWithAttribute(profiledata, "repetitiongroup", "id", dive.RepetitionGroup.ToString());
                    group = dive.RepetitionGroup;
                }

                XmlNode xDive = AddNodeWithAttribute(repetitiongroup, "dive", "id", dive.DiveDateTime.ToString("s"));

                XmlNode xInfoBefore = AddNode(xDive, "informationbeforedive");

                AddNodeWithInnerText(xInfoBefore, "divenumber", dive.DiveNumber.ToString());

                AddNodeWithInnerText(xInfoBefore, "datetime", dive.DiveDateTime.ToString("s"));

                AddNodeWithInnerText(xInfoBefore, "surfacepressure", dive.AirP.ToString());

                XmlNode xSamples = AddNode(xDive, "samples");

                XmlNode xWaypoint = null;
                XmlNode xWaypointFirst = null;

                file.BaseStream.Position = dive.DivePositionBeg;

                bWPOpen = false;

                xWaypoint = uddfDoc.CreateElement("waypoint");

                byte[] buffer = new byte[dive.DivePositionEnd - dive.DivePositionBeg];
                int length = (int)dive.DivePositionEnd - (int)dive.DivePositionBeg + 2;
                buffer = file.ReadBytes(length);
                
                currTime = 0;
                for (int j = 0; j < length; j++)
                {

                    bt = buffer[j];

                    switch (bt)
                    {
                        case 0xF1: // Температура
                            bt = buffer[++j];
                            xTmp = AddNodeWithInnerText(xWaypoint, "temperature", String.Format(CultureInfo.InvariantCulture, "{0:F1}", (double)(273 + bt + 0.1)));
                            if (xWaypointFirst.SelectSingleNode("temperature") == null) xWaypointFirst.AppendChild(xTmp);
                            break;
                        case 0xF2:
                            bt = buffer[++j];
                            break;
                        case 0xF3:
                            bt = buffer[++j];
                            break;
                        case 0xF4: //Смена газа
                            bt = buffer[++j];
                            helium = bt;
                            bt = buffer[++j];
                            if (bt == 0xF4)
                            {
                                bt = buffer[++j];
                                oxygen = bt;
                                AddNodeWithAttribute(xWaypoint, "switchmix", "ref", getGas(oxygen, helium));
                            }
                            break;
                        case 0xF5:
                            bt = buffer[++j];
                            break;
                        case 0xF6: //Warning flags
                            bt = buffer[++j];
                            int iTmp1 = bt;
                            if ( (iTmp1 & 0x04) == 04) // PO2!
                            {
                                AddNodeWithInnerText(xWaypoint, "alarm", "error");
                            }
                            if ( (iTmp1 & 0x02) == 02) // Fast!
                            {
                                AddNodeWithInnerText(xWaypoint, "alarm", "ascent");
                            }
                            if ( (iTmp1 & 0x01) == 01) // Deco!
                            {
                                AddNodeWithInnerText(xWaypoint, "alarm", "deco");
                            }                            
                            break;
                        case 0xF7: //Динамический потолок
                            bt = buffer[++j];

                            XmlNode xDecostop = uddfDoc.CreateElement("decostop");

                            XmlAttribute xAttr = uddfDoc.CreateAttribute("kind"); // создаём атрибут
                            xAttr.Value = "mandatory";
                            xDecostop.Attributes.Append(xAttr);

                            xAttr = uddfDoc.CreateAttribute("decodepth");
                            xAttr.Value = bt.ToString() + ".0";
                            xDecostop.Attributes.Append(xAttr);

                            xAttr = uddfDoc.CreateAttribute("duration");
                            xAttr.Value = "60.0";
                            xDecostop.Attributes.Append(xAttr);

                            xWaypoint.AppendChild(xDecostop);

                            break;
                        case 0xF8:
                            bt = buffer[++j];
                            break;
                        case 0xF9: // NDL/TTS
                            bt = buffer[++j];
                            iTmp1 = bt;
                            iTmp1 = iTmp1 & 0x0F;
                            if (iTmp1 > 0 && bt >= 240)
                            {
                                AddNodeWithInnerText(xWaypoint, "nodecotime", String.Format(CultureInfo.InvariantCulture, "{0:F1}", iTmp1 * 60));
                            }
                            break;
                        case 0xFF:
                            if (bWPOpen)
                            {
                                xSamples.AppendChild(xWaypoint);
                                xWaypoint = uddfDoc.CreateElement("waypoint");
                            }
                            bFlgExit = true;
                            break;
                        default: //Значение глубины
                            if (bWPOpen)
                            {
                                xSamples.AppendChild(xWaypoint);
                                xWaypoint = uddfDoc.CreateElement("waypoint");
                            }
                            
                            if (xWaypointFirst == null) xWaypointFirst = xWaypoint;

                            AddNodeWithInnerText(xWaypoint, "depth", bt.ToString() + ".0");
                            AddNodeWithInnerText(xWaypoint, "divetime", currTime.ToString() + ".0");

                            currTime += 10;
                            bWPOpen = true;
                            break;
                    }

                    if (bFlgExit)
                    {
                        break;
                    }

                }

                XmlNode xInfoAfter = AddNode(xDive, "informationafterdive");

                AddNodeWithInnerText(xInfoAfter, "greatestdepth", dive.MaxDepth.ToString());

                AddNodeWithInnerText(xInfoAfter, "averagedepth", String.Format(CultureInfo.InvariantCulture, "{0:F1}", (double)(dive.AvgDepth / 10.0)));

                AddNodeWithInnerText(xInfoAfter, "lowesttemperature", String.Format(CultureInfo.InvariantCulture, "{0:F1}", (double)(273.0 + (double)dive.MinTemp)));

                AddNodeWithInnerText(xInfoAfter, "diveduration", String.Format(CultureInfo.InvariantCulture, "{0:F1}", dive.DiveDuration * 60));
            }

            uddfDoc.Save(uddfFile);
            Console.WriteLine(String.Format("Переведено в формат UDDF {0} погружений.", dives.Count));
            Console.WriteLine(String.Format("UDDF Файл: {0}.", uddfFile));

        }
    }
}
