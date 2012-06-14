using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TShockAPI;
using Terraria;
namespace AdminTools
{
    public class BindTool
    {
        public List<string> commands;
        public Item item;
        public bool looping;
        private int count;
        public BindTool(Item i, List<string> cmd, bool loop = false)
        {
            this.item = i;
            this.commands = cmd;
            this.looping = loop;
            this.count = 0;
        }
        public void DoCommand(TSPlayer player)
        {
            try
            {
                if (looping)
                {
                    TShockAPI.Commands.HandleCommand(player, this.commands[this.count]);
                    this.count++;
                    if (this.count >= this.commands.Count)
                        this.count = 0;
                }
                else
                {
                    foreach (String cmd in this.commands)
                    {
                        TShockAPI.Commands.HandleCommand(player, cmd);
                    }
                }
            }
            catch (Exception ex) { Log.ConsoleError(ex.ToString()); }
        }
    }  
}
