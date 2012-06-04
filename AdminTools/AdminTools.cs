using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Data;
using System.ComponentModel;
using Terraria;
using Hooks;
using TShockAPI;
using TShockAPI.DB;
using Mono.Data.Sqlite;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using System.Threading;

namespace AdminTools
{
    public class ATPlayer
    {
        public int UserID;
        public String AccountName;
        public List<String> IP;
        public List<String> Nicknames;
        public DateTime LastSeen;
        public bool Online;
        public List<BindTool> BindTools = new List<BindTool>();
        public ATPlayer(int id, string name, List<string> nick, List<string> ip, DateTime lastseen)
        {
            this.UserID = id;
            this.AccountName = name;
            this.IP = ip;
            this.Nicknames = nick;
            this.LastSeen = lastseen;
            this.Online = false;
        }
        public ATPlayer(int id, string name, List<string> nick, List<string> ip)
        {
            this.UserID = id;
            this.AccountName = name;
            this.IP = ip;
            this.Nicknames = nick;
            this.Online = true; 
        }
        public void AddBindTool(BindTool NewBT)
        {
            foreach (BindTool PT in this.BindTools)
            {
                if (PT.item == NewBT.item)
                {
                    this.BindTools.Remove(PT);
                    break;
                }
            }
            this.BindTools.Add(NewBT);
        }
        public BindTool GetBindTool(Item item)
        {
            foreach (BindTool bt in this.BindTools)
            {
                if (bt.item.netID == item.netID)
                    return bt;
            }
            return null;
        }
        public void RemoveBindTool(Item item)
        {
            for (int i = 0; i < this.BindTools.Count; i++)
            {
                if (this.BindTools[i].item.netID == item.netID)
                {
                    this.BindTools.RemoveAt(i);
                    return;
                }
            }
        }
    }
    [APIVersion(1, 12)]
    public class AdminToolsMain : TerrariaPlugin
    {
        public static IDbConnection db;
        public static SqlTableCreator SQLcreator;
        public static SqlTableEditor SQLeditor;
        private static string savepath = Path.Combine(TShock.SavePath, "AdminTools/");
        public static List<ATPlayer> PlayerList = new List<ATPlayer>();
        


        public override string Name
        {
            get { return "AdminTools"; }
        }
        public override string Author
        {
            get { return "by InanZen"; }
        }
        public override string Description
        {
            get { return "Provides a number of useful tools for administrators"; }
        }
        public override Version Version
        {
            get { return new Version("0.1"); }
        }
        public AdminToolsMain(Main game)
            : base(game)
        {
            Order = -1;
        }
        public override void Initialize()
        {
            NetHooks.GetData += GetData;
            ServerHooks.Leave += OnLeave;
            ServerHooks.Chat += OnChat;
            //GameHooks.Update += OnUpdate;
            //NetHooks.SendData += SendData;
          //  Commands.ChatCommands.Add(new Command("permission", CommandMethod, "command"));
            if (!Directory.Exists(savepath))
                Directory.CreateDirectory(savepath);
            SetupDb();
            Commands.ChatCommands.Add(new Command("AT.whois", Whois, "whois"));
            Commands.ChatCommands.Add(new Command("AT.bindtool", BindToolCMD, "bindtool", "bt"));
            

        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                NetHooks.GetData -= GetData;
                ServerHooks.Leave -= OnLeave;
                ServerHooks.Chat -= OnChat;
                //GameHooks.Update -= OnUpdate;
                //NetHooks.SendData -= SendData;
            }
            base.Dispose(disposing);
        }
        private void SetupDb()
        {
            if (TShock.Config.StorageType.ToLower() == "sqlite")
            {
                string sql = Path.Combine(savepath, "AdminTools.sqlite");
                db = new SqliteConnection(string.Format("uri=file://{0},Version=3", sql));
            }
            else if (TShock.Config.StorageType.ToLower() == "mysql")
            {
                try
                {
                    var hostport = TShock.Config.MySqlHost.Split(':');
                    db = new MySqlConnection();
                    db.ConnectionString =
                        String.Format("Server={0}; Port={1}; Database={2}; Uid={3}; Pwd={4};",
                                      hostport[0],
                                      hostport.Length > 1 ? hostport[1] : "3306",
                                      TShock.Config.MySqlDbName,
                                      TShock.Config.MySqlUsername,
                                      TShock.Config.MySqlPassword
                            );
                }
                catch (MySqlException ex)
                {
                    Log.Error(ex.ToString());
                    throw new Exception("MySql not setup correctly");
                }
            }
            else
            {
                throw new Exception("Invalid storage type");
            }

            SQLcreator = new SqlTableCreator(db, new SqliteQueryCreator());
            SQLeditor = new SqlTableEditor(db, new SqliteQueryCreator());

            var table = new SqlTable("SignData",
                new SqlColumn("ID", MySql.Data.MySqlClient.MySqlDbType.Int32) { Primary = true, AutoIncrement = true, NotNull = true },
                new SqlColumn("X", MySql.Data.MySqlClient.MySqlDbType.Int32),
                new SqlColumn("Y", MySql.Data.MySqlClient.MySqlDbType.Int32),
                new SqlColumn("User", MySql.Data.MySqlClient.MySqlDbType.VarChar) { Length = 30 },
                new SqlColumn("Time", MySql.Data.MySqlClient.MySqlDbType.Int32),
                new SqlColumn("WorldID", MySql.Data.MySqlClient.MySqlDbType.Int32)
            );
            SQLcreator.EnsureExists(table);
            table = new SqlTable("PlayerData",
                new SqlColumn("UserID", MySql.Data.MySqlClient.MySqlDbType.Int32) { Primary = true, Unique = true, NotNull = true },
                new SqlColumn("Username", MySql.Data.MySqlClient.MySqlDbType.VarChar) { Length = 30 },
                new SqlColumn("Nicknames", MySql.Data.MySqlClient.MySqlDbType.Text),
                new SqlColumn("IPs", MySql.Data.MySqlClient.MySqlDbType.Text),
                new SqlColumn("LastSeen", MySql.Data.MySqlClient.MySqlDbType.Int32)
            );
            SQLcreator.EnsureExists(table);

        }
        private static void OnLeave(int who)
        {
            try
            {
                if (TShock.Players[who].UserID != -1)
                {
                    var player = GetPlayerByUserID(TShock.Players[who].UserID);
                    if (player != null)
                    {
                        AdminToolsMain.db.QueryReader("UPDATE PlayerData SET Nicknames=@1, IPs=@2, LastSeen=@3 WHERE UserID=@0", player.UserID, JsonConvert.SerializeObject(player.Nicknames, Formatting.None), JsonConvert.SerializeObject(player.IP, Formatting.None), DateTime.Now.Ticks);

                        lock (PlayerList)
                        {
                            PlayerList.Remove(player);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.ConsoleError(ex.Message);
            }
        }
        public void OnChat(messageBuffer buffer, int who, string text, System.ComponentModel.HandledEventArgs args)
        {
            if (args.Handled)
                return;
            if (text.StartsWith("/login"))
            {
                if (text.Length > 7 && TShock.Players[who].UserID != -1)
                {
                    var player = GetPlayerByUserID(TShock.Players[who].UserID);
                    if (player != null)
                    {
                        try
                        {
                            AdminToolsMain.db.QueryReader("UPDATE PlayerData SET Nicknames=@1, IPs=@2, LastSeen=@3 WHERE UserID=@0", player.UserID, JsonConvert.SerializeObject(player.Nicknames, Formatting.None), JsonConvert.SerializeObject(player.IP, Formatting.None), DateTime.Now.Ticks);

                            lock (PlayerList)
                            {
                                PlayerList.Remove(player);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.ConsoleError(ex.Message);
                        }
                    }
                    LoginThread lt = new LoginThread(who);
                    Thread t = new Thread(new ThreadStart(lt.CheckLogin));
                    t.Start();
                }
            }
        }


        #region Getdata
        public static void GetData(GetDataEventArgs e)
        {
            if (e.Handled)
                return;
            try
            {
                switch (e.MsgID)
                {
                    #region player join
                    case PacketTypes.TileGetSection:
                        {
                            if (!TShock.Players[e.Msg.whoAmI].RequestedSection)
                            {
                                LoginThread lt = new LoginThread(e.Msg.whoAmI);
                                Thread t = new Thread(new ThreadStart(lt.CheckLogin));
                                t.Start();
                            }
                            break;
                        }
                    #endregion
                    #region Tile Edit
                    case PacketTypes.Tile:
                        {
                            byte type, tileType, style;
                            Int32 x, y;
                            bool fail;
                            using (var data = new MemoryStream(e.Msg.readBuffer, e.Index, e.Length))
                            {
                                var reader = new BinaryReader(data);
                                type = reader.ReadByte();
                                x = reader.ReadInt32();
                                y = reader.ReadInt32();
                                tileType = reader.ReadByte();
                                fail = reader.ReadBoolean();
                                style = reader.ReadByte();
                                reader.Close();
                                reader.Dispose();
                            }
                            int signID = Sign.ReadSign(x, y);
                            //Console.WriteLine("TileEdit: type: {0}, tiletype: {1}, fail: {2}, style: {3}, tile.frameX: {4}", type, tileType, fail, style, Main.tile[x, y].frameX);
                            //  Tile tile = Main.tile[x, y];
                            //    Console.WriteLine("Tileinfo: type: {0}, frameX: {1}, frameY: {2}, frameNum: {3}", tile.type, tile.frameX, tile.frameY, tile.frameNumber);
                            //   Console.WriteLine("Tiledata: type: {0}, frameX: {1}, frameY: {2}, frameNum: {3}", tile.Data.type, tile.Data.frameX, tile.Data.frameY, tile.Data.frameNumber);
                            //    Console.WriteLine("type: {0}, Main.tile[].active: {1}, fail: {2}", type, Main.tile[x, y].Data.active, fail);
                            #region Tile Remove
                            if (type == 0 || type == 4)
                            {
                                if (tileType == 0 && signID != -1)
                                {
                                    db.QueryReader("DELETE FROM SignData WHERE X=@0 AND Y=@1 AND WorldID=@2", x, y, Main.worldID);
                                }
                            }
                            #endregion
                            #region Tile place
                            else if (type == 1)
                            {

                            }
                            #endregion
                            break;
                        }
                    #endregion
                    #region Sign Edit
                    case PacketTypes.SignNew:
                        {
                            Int16 signId;
                            Int32 x, y;
                            byte[] textB;
                            using (var data = new MemoryStream(e.Msg.readBuffer, e.Index, e.Length))
                            {
                                var reader = new BinaryReader(data);
                                signId = reader.ReadInt16();
                                x = reader.ReadInt32();
                                y = reader.ReadInt32();
                                textB = reader.ReadBytes(e.Length - 10);
                                reader.Close();
                                reader.Dispose();
                            }
                            string newtxt = Encoding.UTF8.GetString(textB);
                            var tplayer = TShock.Players[e.Msg.whoAmI];
                            int id = Sign.ReadSign(x, y);
                            if (id != -1)
                            {
                                try
                                {
                                    lock (db)
                                    {
                                        var values = new List<SqlValue>();
                                        values.Add(new SqlValue("X", x));
                                        values.Add(new SqlValue("Y", y));
                                        values.Add(new SqlValue("User", "'" + tplayer.Name + "'"));
                                        values.Add(new SqlValue("Time", DateTime.Now.Ticks));
                                        values.Add(new SqlValue("WorldID", Main.worldID));
                                        SQLeditor.InsertValues("SignData", values);
                                        /*System.Data.SqlClient.SqlConnection conn = new System.Data.SqlClient.SqlConnection();
                                        System.Data.SqlClient.SqlCommand cmd = new System.Data.SqlClient.SqlCommand("INSERT INTO user (infoDate) VALUES (@value)", conn);
                                        cmd.Parameters.AddWithValue("@value", DateTime.Now);
                                        cmd.ExecuteNonQuery();*/
                                    }
                                }
                                catch (Exception ex)
                                { Log.ConsoleError(ex.Message); }
                            }
                            break;
                        }
                    #endregion
                    #region Sign Read
                    case PacketTypes.SignRead:
                        {
                            int x, y;
                            using (var data = new MemoryStream(e.Msg.readBuffer, e.Index, e.Length))
                            {
                                var reader = new BinaryReader(data);
                                x = reader.ReadInt32();
                                y = reader.ReadInt32();

                                reader.Close();
                            }
                            var id = Sign.ReadSign(x, y);
                            var tplayer = TShock.Players[e.Msg.whoAmI];
                            if (tplayer != null && id != -1)
                            {
                                if (tplayer.Group.HasPermission("AT.signhistory"))
                                {
                                    QueryResult query = db.QueryReader("Select User,Time from SignData where X=@0 AND Y=@1 AND WorldID=@2", x, y, Main.worldID);
                                    tplayer.SendMessage(String.Format("Sign edit history:"), Color.LightSalmon);
                                    while (query.Read())
                                    {
                                        long time = query.Get<long>("Time");
                                        tplayer.SendMessage(String.Format("{0} @ {1}", query.Get<string>("User"), new DateTime(time)), Color.BurlyWood);
                                    }
                                    query.Dispose();
                                }
                            }
                            // Console.WriteLine("x: {0} y: {1}", x, y);                            
                            break;
                        }
                    #endregion
                    #region Player Update
                    case PacketTypes.PlayerUpdate:
                        {
                            using (var data = new MemoryStream(e.Msg.readBuffer, e.Index, e.Length))
                            {
                                var reader = new BinaryReader(data);
                                byte plyID = reader.ReadByte();
                                byte flags = reader.ReadByte();
                                byte item = reader.ReadByte();
                                float posX = reader.ReadSingle();
                                float posY = reader.ReadSingle();
                                float velX = reader.ReadSingle();
                                float velY = reader.ReadSingle();
                                reader.Close();
                                reader.Dispose();
                                //Console.WriteLine("PlayerUpdate: playerID: {0}, item: {1}, posX: {2}, posY: {3}, velX: {4}, velY: {5}", plyID, item, posX, posY, velX, velY);
                                //Console.WriteLine("item info: Name: {0}, dmg: {1}, animation: {2}", Main.player[plyID].inventory[item].name, Main.player[plyID].inventory[item].damage, Main.player[plyID].inventory[item].useAnimation);
                                TSPlayer tsplayer = TShock.Players[plyID];
                                if (tsplayer == null)
                                    break;
                                ATPlayer player = GetPlayerByUserID(tsplayer.UserID);
                                if (player == null)
                                    break;
                                // Console.WriteLine("Flags: {0}", flags);
                                if ((flags & 32) == 32)
                                {
                                    try
                                    {
                                        var BT = player.GetBindTool(Main.player[plyID].inventory[item]);
                                        if (BT != null)
                                        {
                                            BT.DoCommand(tsplayer);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.ConsoleError(ex.Message);
                                    }
                                    //Console.WriteLine("Player {0} used item: {1}", player.TSPlayer.Name, Main.player[plyID].inventory[item].name);
                                }
                            }
                            break;
                        }
                    #endregion
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
        #endregion
        private void Whois(CommandArgs args)
        {
            if (args.Parameters.Count > 0)
            {
                string name = String.Join(" ", args.Parameters);
                var Results = GetPlayerByName(name);
                if (Results.Count > 6)
                {
                    args.Player.SendMessage("More than 6 results found, please refine your search", Color.LightSalmon);
                }
                else if (Results.Count > 0)
                {
                    args.Player.SendMessage(String.Format("Listing matches ({0}):", Results.Count), Color.LightSalmon);
                    foreach (ATPlayer player in Results)
                    {
                        args.Player.SendMessage(String.Format("({0}){1} ({2}) - Known as: {3} Last online: {4}", player.UserID,player.AccountName,player.IP[0], String.Join(",", player.Nicknames),(player.Online)?"Now":player.LastSeen.ToString()), Color.BurlyWood);
                    }
                }
                else
                {
                    args.Player.SendMessage(String.Format("Cannot find player '{0}'", name), Color.LightSalmon);
                }
                return;
            }
            args.Player.SendMessage("Syntax: /whois player name", Color.Red);
        }
        private static ATPlayer GetPlayerByID(int id)
        {
            int userid = -1;
            if (TShock.Players[id] != null)
                userid = TShock.Players[id].UserID; 
            lock (PlayerList)
            {
                foreach (ATPlayer player in PlayerList)
                    if (player.UserID == id)
                        return player;
            }
            return null;
        } 
        private static ATPlayer GetPlayerByUserID(int id)
        {
            lock (PlayerList)
            {
                foreach (ATPlayer player in PlayerList)
                    if (player.UserID == id)
                        return player;
            }
            return null;
        }        
        private static List<ATPlayer> GetPlayerByName(string name)
        {
            List<ATPlayer> ReturnList = new List<ATPlayer>();
            lock (PlayerList)
            {
                foreach (ATPlayer player in PlayerList)
                {
                    if (player.AccountName.ToLower().StartsWith(name.ToLower()))
                        ReturnList.Add(player);
                    else
                    {
                        foreach (string nick in player.Nicknames)
                        {
                            if (nick.ToLower().StartsWith(name.ToLower()))
                                ReturnList.Add(player);
                        }
                    }
                }
            }
            QueryResult reader = AdminToolsMain.db.QueryReader("SELECT * from PlayerData WHERE Username LIKE '%" + name.ToLower() + "%' OR Nicknames LIKE '%" + name.ToLower() + "%'");
            while (reader.Reader.Read())
            {
                bool found = false;
                foreach (ATPlayer ply in ReturnList)
                {
                    if (ply.AccountName.ToLower() == reader.Get<String>("Username").ToLower())
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                    ReturnList.Add(new ATPlayer(reader.Get<int>("UserID"), reader.Get<String>("Username"), JsonConvert.DeserializeObject<List<String>>(reader.Get<String>("Nicknames")), JsonConvert.DeserializeObject<List<String>>(reader.Get<String>("IPs")), new DateTime(reader.Get<long>("LastSeen"))));
            }
            return ReturnList;
        }

        private static void BindToolCMD(CommandArgs args)
        {
            var player = GetPlayerByUserID(args.Player.UserID);
            if (player == null)
                return;
            if (args.Parameters.Count > 0)
            {
                if (args.Player.TPlayer.selectedItem > 9)
                {
                    args.Player.SendMessage("Please select an item from your Quickbar:", Color.Red);
                    return;
                }
                byte flagmod = 0;
                bool looping = false;
                bool clear = false;
                if (args.Parameters[0].StartsWith("-"))
                {
                    flagmod = 1;
                    for (int i = 1; i < args.Parameters[0].Length; i++)
                    {
                        if (args.Parameters[0][i] == 'l')
                            looping = true;
                        else if (args.Parameters[0][i] == 'c')
                            clear = true;
                        else
                        {
                            args.Player.SendMessage("Invalid BindTool flag.", Color.LightSalmon);
                            args.Player.SendMessage("Valid flags are -l [looping] -c [clear]:", Color.BurlyWood);
                            return;
                        }
                    }
                }
                var item = args.Player.TPlayer.inventory[args.Player.TPlayer.selectedItem];
                if (clear)
                {
                    player.RemoveBindTool(item);
                    args.Player.SendMessage(String.Format("All commands have been removed from {0}", item.name), Color.BurlyWood);
                    return;
                }
                else if (args.Parameters.Count < 2)
                {
                    args.Player.SendMessage("Missing commands", Color.LightSalmon);
                    return;
                }

                var cmdstring = String.Join(" ", args.Parameters.GetRange(flagmod, args.Parameters.Count - flagmod));
                List<string> cmdlist = cmdstring.Split(';').ToList();
                player.AddBindTool(new BindTool(item, cmdlist, looping));
                StringBuilder builder = new StringBuilder(100);
                builder.Append("Bound");
                foreach (string cmd in cmdlist)
                {
                    builder.AppendFormat(" '{0}'", cmd);
                }
                builder.AppendFormat(" to {0}", item.name);
                args.Player.SendMessage(builder.ToString(), Color.BurlyWood);
                return;
            }
            args.Player.SendMessage("BindTool usage:", Color.LightSalmon);
            args.Player.SendMessage("/bindtool [-lc] commands; separated; by semicolon", Color.BurlyWood);
            args.Player.SendMessage("This will bind those commands to the current item in hand.", Color.BurlyWood);
            args.Player.SendMessage("-l Will loop trough commands in order", Color.BurlyWood);
            args.Player.SendMessage("-c Will clear all commands from the item", Color.BurlyWood);
        }
    }



    public class LoginThread
    {
        private int Index;
        public LoginThread(int index)
        {
            this.Index = index;
        }
        public void CheckLogin()
        {
            System.Threading.Thread.Sleep(3000);
            try
            {
                TSPlayer player = TShock.Players[this.Index];
                //if (RolePlay.debugInfo) Console.WriteLine("--- > UserID: {0}", this.RPlayer.TSPlayer.UserID);                  
                if (player != null && player.UserID != -1)
                {
                    QueryResult reader = AdminToolsMain.db.QueryReader("SELECT * from PlayerData WHERE UserID=@0", player.UserID);
                    List<String> IP;
                    List<String> Nicknames;
                    if (reader.Read())
                    {
                        Console.WriteLine("Found player data for {0}", player.Name);
                        IP = JsonConvert.DeserializeObject<List<String>>(reader.Get<String>("IPs"));
                        Nicknames = JsonConvert.DeserializeObject<List<String>>(reader.Get<String>("Nicknames"));
                    }
                    else
                    {
                        IP = new List<string>();
                        Nicknames = new List<string>();
                        IP.Add(player.IP);
                        if (player.UserAccountName != player.Name)
                            Nicknames.Add(player.Name);
                        AdminToolsMain.db.QueryReader("INSERT INTO PlayerData (UserID, Username, Nicknames, IPs, LastSeen) VALUES (@0, @1, @2, @3, @4)", player.UserID, player.UserAccountName, JsonConvert.SerializeObject(Nicknames, Formatting.None), JsonConvert.SerializeObject(IP, Formatting.None), DateTime.Now.Ticks);
                    }
                    reader.Dispose();
                    if (!IP.Contains(player.IP))
                    {
                        IP.Add(player.IP);
                    }
                    else if (player.UserAccountName != player.Name && !Nicknames.Contains(player.Name))
                    {
                        Nicknames.Add(player.Name);
                    }
                    lock (AdminToolsMain.PlayerList)
                    {
                        AdminToolsMain.PlayerList.Add(new ATPlayer(player.UserID, player.UserAccountName, Nicknames, IP));
                    }

                }
            }
            catch (Exception e)
            {
                Log.ConsoleError(e.Message);
            }
        }
    }

}
