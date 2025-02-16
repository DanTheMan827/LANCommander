﻿namespace LANCommander.SDK.Models
{
    public class Archive : BaseModel
    {
        public string Changelog { get; set; }

        public string ObjectKey { get; set; }

        public string Version { get; set; }

        public virtual Game Game { get; set; }

        public virtual Archive LastVersion { get; set; }
    }
}
