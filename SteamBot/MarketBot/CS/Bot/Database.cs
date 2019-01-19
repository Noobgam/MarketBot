using CSGOTM;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static CSGOTM.Logic;

namespace SteamBot.MarketBot.CS.Bot {
    public abstract class BotDatabase {
        public readonly ReaderWriterLockSlim _DatabaseLock = new ReaderWriterLockSlim();
        public void EnterReadLock() {
            _DatabaseLock.EnterReadLock();
        }

        public void EnterWriteLock() {
            _DatabaseLock.EnterWriteLock();
        }

        public void ExitReadLock() {
            _DatabaseLock.ExitReadLock();
        }

        public void ExitWriteLock() {
            _DatabaseLock.ExitWriteLock();
        }

        ~BotDatabase() {
            _DatabaseLock.Dispose();
        }
    }
    public class SalesDatabase : BotDatabase {
        private NewMarketLogger Log = new NewMarketLogger("SalesDatabase");
        public Dictionary<string, BasicSalesHistory> newDataBase = new Dictionary<string, BasicSalesHistory>();

        private readonly string PREFIXPATH;
        private readonly string NEWDATABASEPATH;
        private readonly string NEWDATABASETEMPPATH;

        public SalesDatabase(string path = "common") {
            PREFIXPATH = path;

            NEWDATABASEPATH = Path.Combine(PREFIXPATH, "newdatabase.txt");
            NEWDATABASETEMPPATH = Path.Combine(PREFIXPATH, "newdatabaseTemp.txt");
        }

        public void Save() {
            if (File.Exists(NEWDATABASEPATH))
                File.Copy(NEWDATABASEPATH, NEWDATABASETEMPPATH, true);
            Log.Info($"Size of db is {newDataBase.Count}");
            try {
                EnterReadLock();
                BinarySerialization.NS.Serialize(NEWDATABASEPATH, newDataBase);
            } finally {
                ExitReadLock();
            }
        }

        public bool DownloadFromJuggler() {
            try {
                byte[] resp = StringUtils.FromBase64(LocalRequest.GetDatabase());
                newDataBase = BinarySerialization.NS.Deserialize<Dictionary<string, BasicSalesHistory>>(resp);
                Log.Success("Loaded new DB from juggler. Total item count: " + newDataBase.Count);
                return true;
            } catch (Exception) {
                return false;
            }
        }
        
        public bool MoveFromTemp() {
            if (!File.Exists(NEWDATABASETEMPPATH)) {
                return false;
            }
            try {
                File.Delete(NEWDATABASEPATH);
                File.Move(NEWDATABASETEMPPATH, NEWDATABASEPATH);
                return true;
            } catch (Exception) {
                return false;
            }
        }

        public bool Load() {
            if (!File.Exists(NEWDATABASEPATH)) {
                if (!MoveFromTemp()) {
                    return DownloadFromJuggler();
                }
            }
            try {
                EnterWriteLock();
                newDataBase = BinarySerialization.NS.Deserialize<Dictionary<string, BasicSalesHistory>>(NEWDATABASEPATH);
                if (File.Exists(NEWDATABASETEMPPATH))
                    File.Delete(NEWDATABASETEMPPATH);
                Log.Success("Loaded new DB from cache. Total item count: " + newDataBase.Count);
                return true;
            } catch (Exception ex) {
                Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                return true;
            } finally {
                ExitWriteLock();
            }
        }
    }
    public class EmptyStickeredDatabase : BotDatabase {

        public HashSet<Tuple<long, long>> unstickeredCache = new HashSet<Tuple<long, long>>();
        private NewMarketLogger Log = new NewMarketLogger("EmptyStickeredDatabase");

        public bool NoStickers(string cid, string iid) {
            return NoStickers(long.Parse(cid), long.Parse(iid));
        }

        public bool NoStickers(long cid, long iid) {
            return unstickeredCache.Contains(new Tuple<long, long>(cid, iid));
        }

        public void Add(long cid, long iid) {
            if (NoStickers(cid, iid))
                return;
            unstickeredCache.Add(new Tuple<long, long>(cid, iid));
        }

        public string[] Dump() {
            string[] res = new string[unstickeredCache.Count];
            int id = 0;
            foreach (var tup in unstickeredCache) {
                res[id++] = tup.Item1 + "_" + tup.Item2;
            }
            return res;
        }

        public bool LoadFromArray(string[] arr) {
            try {
                unstickeredCache = new HashSet<Tuple<long, long>>();
                foreach (var tup in arr) {
                    string[] splitter = tup.Split('_');
                    unstickeredCache.Add(new Tuple<long, long>(long.Parse(splitter[0]), long.Parse(splitter[1])));
                }
                return true;
            } catch (Exception) {
                return false;
            }
        }

        public bool Load() {
            try {
                unstickeredCache = new HashSet<Tuple<long, long>>();
                byte[] resp = StringUtils.FromBase64(LocalRequest.GetEmptyStickeredDatabase());
                if (LoadFromArray(BinarySerialization.NS.Deserialize<string[]>(resp, true))) {
                    Log.Success("Loaded emptystickered DB from juggler. Total item count: " + unstickeredCache.Count);
                    return true;
                }
                Log.Warn("Could not load unstickered database");
                return false;
            } catch (Exception) {
                return false;
            }
        }
    }
}
