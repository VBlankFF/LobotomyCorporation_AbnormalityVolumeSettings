using LobotomyBaseMod;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Harmony;
using System.Reflection;
using System.Xml;
using System.IO;
using UnityEngine;

// I probably shouldn't write my thoughts as code comments but it keeps it with the code so it shouldn't be so bad
/* Current thoughts:
 * I could have a volume slider for every sound and every abno (should do every abno first)
 * The only way to make this intuitive for the end user is to have a sound player, probably
 * How do I do UI like this?
 * CreatureTypeInfo.Instance.GetData() seems to be the place where basically everything relevant is loaded
 * Parts needed:
 * UI bit with the sound player
 * Something to gather all the abno data
 * Config saver/loader/reader
 * Harmony Patch to apply the sound settings with (SoundEffectPlayer.Play_Mod())
 * CreatureTypeList will be very useful
 */
namespace SoundMuter
{
    public class Harmony_Patch
    {
        public static Dictionary<long, SoundSetting> AbnoSoundSettings;
        public static Dictionary<string, AbnoSound> AllSounds;
        public Harmony_Patch() 
        {
            AbnoSoundSettings = new Dictionary<long, SoundSetting>();
            try
            {
                HarmonyInstance patcher = HarmonyInstance.Create("VBlankFF_AbnormalityVolumeSettings");
                HarmonyMethod init = new HarmonyMethod(typeof(Harmony_Patch).GetMethod("Init"));
                Type[] play_modTypes = { typeof(String), typeof(String), typeof(Transform), typeof(Single), typeof(Single), typeof(AudioRolloffMode) };
                Type[] playOnce_modTypes = { typeof(String), typeof(String), typeof(Vector2), typeof(Single), typeof(Single), typeof(AudioRolloffMode) };
                patcher.Patch(typeof(GlobalGameManager).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance), null, init, null);
                HarmonyMethod soundVolumeChanger = new HarmonyMethod(typeof(Harmony_Patch).GetMethod("SoundVolumeChanger", BindingFlags.Public | BindingFlags.Static));
                patcher.Patch(typeof(SoundEffectPlayer).GetMethod("Play_Mod", BindingFlags.Public | BindingFlags.Static, null, play_modTypes, null), soundVolumeChanger, null);
                patcher.Patch(typeof(SoundEffectPlayer).GetMethod("PlayOnce_Mod", BindingFlags.Public | BindingFlags.Static, null, playOnce_modTypes, null), soundVolumeChanger, null);
            }
            catch (Exception e)
            {
                ModDebug.Log("AbnormalityVolumeSettings has encountered an error on initialization: " + e.ToString());
            }
        }
        public static void Init()
        {
            foreach(DirectoryInfo dir in new DirectoryInfo(Application.dataPath + @"/BaseMods").GetDirectories())
            { 
                if (dir.Name.StartsWith("AbnormalityVolumeSettings_VBlankFF"))
                {
                    AbnoSoundSettings = new Dictionary<long, SoundSetting>();
                    AllSounds = new Dictionary<string, AbnoSound>();
                    string path = dir.FullName + @"/Config/config.xml";
                    foreach (CreatureTypeInfo info in CreatureTypeList.instance.GetList())
                    {
                        long id = info.id;
                        foreach (KeyValuePair<string, string> kvp in info.soundTable)
                        {
                            AllSounds[kvp.Value] = new AbnoSound(id, new SoundSetting());
                        }
                        AbnoSoundSettings[id] = new SoundSetting();
                    }
                    ConfigManager.Load(AbnoSoundSettings, AllSounds, path);
                    ConfigManager.Save(AbnoSoundSettings, AllSounds, path);
                    break;
                } 
            }
        }
        public static void SoundVolumeChanger(string soundname, ref float volume)
        {
            AbnoSound soundconfig;
            if (!AllSounds.TryGetValue(soundname, out soundconfig))
            {
                ModDebug.Log("AbnormalityVolumeSettings: Unknown sound " + soundname);
                return;
            }
            if (soundconfig.setting.muted || AbnoSoundSettings[soundconfig.abnoid].muted)
            {
                volume = 0f;
                return;
            }
            volume *= soundconfig.setting.volume;
            SoundSetting abnoSoundSetting;
            if (!AbnoSoundSettings.TryGetValue(soundconfig.abnoid, out abnoSoundSetting))
            {
                return;
            }
            volume *= abnoSoundSetting.volume;
        }
        public AbnoSound GetFromSounds(string soundname)
        {
            AbnoSound value;
            if (AllSounds.TryGetValue(soundname, out value))
            {
                return value;
            }
            ModDebug.Log("AbnormalityVolumeSettings: " + soundname + " is not a recognized sound");
            return null;
        }
        public SoundSetting GetAbnoSetting(long id)
        {
            SoundSetting value;
            if (AbnoSoundSettings.TryGetValue(id, out value))
            {
                return value;
            }
            ModDebug.Log("AbnormalityVolumeSettings: " + id + " is not a recognized abnormality id");
            return null;
        }
    }
    public class SoundMuter
    {
        public SoundMuter() { }
    }
    public class SoundSetting
    {
        public float volume;
        public bool muted;
        public SoundSetting() 
        {
            volume = 1.0f;
            muted = false;
        }
        public SoundSetting(float volume, bool muted)
        {
            this.volume = volume;
            this.muted = muted;
        }
    }
    public class AbnoSound
    {
        public long abnoid;
        public SoundSetting setting;
        public AbnoSound()
        {
            this.abnoid = -1;
            this.setting = new SoundSetting();
        }
        public AbnoSound(long abnoid)
        {
            this.abnoid = abnoid;
            this.setting = new SoundSetting();
        }
        public AbnoSound(long abnoid, SoundSetting setting)
        {
            this.abnoid = abnoid;
            this.setting = setting;
        }
    }
    public static class ConfigManager
    {
        public static void Save(Dictionary<long, SoundSetting> abnoSettings, Dictionary<string, AbnoSound> soundSpecificSettings, string path)
        {
            XmlDocument config = GetConfigFile(path);
            XmlNode rootNode = config.FirstChild;
            foreach (KeyValuePair<string, AbnoSound> kvp in soundSpecificSettings)
            {
                // if we can't find the creatureinfo, the abno is probably not loaded and we shouldn't mess with it
                CreatureTypeInfo info = CreatureTypeList.instance.GetData(kvp.Value.abnoid);
                if (info is null)
                {
                    ModDebug.Log("AbnormalityVolumeSettings: can't find abnormality of id " + kvp.Value.abnoid);
                    continue;
                }
                // get the abnonode by id
                string AbnoName = info.name;
                XmlElement abnonode = (XmlElement)rootNode.SelectSingleNode("id" + kvp.Value.abnoid);
                if (abnonode is null)
                {
                    // make abno node
                    abnonode = config.CreateElement("id" +  kvp.Value.abnoid);
                    rootNode.AppendChild(abnonode);
                    abnonode.SetAttribute("name", AbnoName);
                }
                // get the sound node of the abno node
                XmlElement anAbnoSound = (XmlElement)GetNodeBySrc(kvp.Key, abnonode);
                if (anAbnoSound is null)
                {
                    string soundName = GetSoundNameFromTable(kvp.Key, info);
                    if (soundName == "Unknown")
                    {
                        ModDebug.Log("AbnormalityVolumeSettings: sound of src " + kvp.Key + " could not be found");
                        continue;
                    }
                    anAbnoSound = config.CreateElement(soundName);
                    XmlAttribute muteAttr = config.CreateAttribute("muted");
                    XmlAttribute volumeAttr = config.CreateAttribute("volume");
                    XmlAttribute srcAttr = config.CreateAttribute("src");
                    srcAttr.Value = kvp.Key;
                    anAbnoSound.SetAttributeNode(muteAttr);
                    anAbnoSound.SetAttributeNode(volumeAttr);
                    anAbnoSound.SetAttributeNode(srcAttr);
                    abnonode.AppendChild(anAbnoSound);
                }
                anAbnoSound.SetAttribute("muted", kvp.Value.setting.muted ? "true" : "false");
                anAbnoSound.SetAttribute("volume", kvp.Value.setting.volume.ToString());
            }
            foreach (KeyValuePair<long, SoundSetting> kvp in abnoSettings)
            {
                XmlElement abnonode = (XmlElement)rootNode.SelectSingleNode("id" + kvp.Key.ToString());
                CreatureTypeInfo info = CreatureTypeList.instance.GetData(kvp.Key);
                if (abnonode is null)
                {
                    // make abno node
                    abnonode = config.CreateElement("id" + kvp.Key.ToString());
                    rootNode.AppendChild(abnonode);
                    string abnoName;
                    if (info is null)
                    {
                        abnoName = "Unknown";
                    }
                    else
                    {
                        abnoName = info.name;
                    }
                    abnonode.SetAttribute("name", abnoName);
                }
                if (!(info is null))
                {
                    abnonode.SetAttribute("name", info.name);
                }
                abnonode.SetAttribute("muted", kvp.Value.muted ? "true" : "false");
                abnonode.SetAttribute("volume", kvp.Value.volume.ToString());
            }
            using (FileStream configStream = new FileStream(path, FileMode.Create))
            {
                config.Save(configStream);
            }
            ModDebug.Log("AbnormalityVolumeSettings: Config saved successfully");
        }
        public static void Load(Dictionary<long, SoundSetting> abnoSettings, Dictionary<string, AbnoSound> soundSpecificSettings, string path)
        {
            XmlDocument config = new XmlDocument();
            config = GetConfigFile(path);
            foreach (XmlElement abnoElement in config.FirstChild.ChildNodes)
            {
                long id = long.Parse(abnoElement.Name.Substring(2));
                string abnoVolumeString = abnoElement.GetAttribute("volume");
                if (abnoVolumeString == "") { abnoVolumeString = "1"; }
                float abnoVolume = float.Parse(abnoVolumeString);
                bool abnoMuted = false;
                if (abnoElement.GetAttribute("muted") == "true")
                {
                    abnoMuted = true;
                }
                abnoSettings[id] = new SoundSetting(abnoVolume, abnoMuted);
                foreach(XmlElement soundElement in abnoElement.ChildNodes)
                {
                    string soundVolumeString = soundElement.GetAttribute("volume");
                    if (soundVolumeString == "") { soundVolumeString = "1"; }
                    float soundVolume = float.Parse(soundVolumeString);
                    bool soundMuted = false;
                    if (soundElement.GetAttribute("muted") == "true")
                    {
                        soundMuted = true;
                    }
                    soundSpecificSettings[soundElement.GetAttribute("src")] = new AbnoSound(id, new SoundSetting(soundVolume, soundMuted));
                }
            }
            ModDebug.Log("AbnormalityVolumeSettings: Config loaded successfully");
        }
        public static XmlDocument GetConfigFile(string path)
        {
            XmlDocument config = new XmlDocument();
            if (File.Exists(path))
            {
                try
                {
                    using (FileStream configFileStream = new FileStream(path, FileMode.Open))
                    {
                        config.Load(configFileStream);
                    }
                    return config;
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("AbnormalityVolumeSettings encountered an error while loading the config:" + ex.ToString());
                }
            }
            XmlNode rootNode = config.CreateElement("config");
            config.AppendChild(rootNode);
            return config;
        }
        public static XmlNode GetNodeBySrc(string src, XmlNode containingNode)
        {
            foreach (XmlNode subnode in containingNode.ChildNodes)
            {
                foreach(XmlAttribute attr in subnode.Attributes)
                {
                    if (attr.Name == "src" && attr.InnerText == src)
                    {
                        return subnode;
                    }
                }
            }
            return null;
        }
        public static string GetSoundNameFromTable(string src, CreatureTypeInfo info)
        {
            foreach (KeyValuePair<string, string> soundTableKVP in info.soundTable)
            {
                if (soundTableKVP.Value == src)
                {
                    return soundTableKVP.Key;
                }
            }
            return "Unknown";
        }
    }
}
