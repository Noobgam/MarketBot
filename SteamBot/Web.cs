
public class Pair<T, U>
{
    public Pair() { }

    public Pair(T first, U second)
    {
        this.First = first;
        this.Second = second;
    }

    public T First { get; set; }
    public U Second { get; set; }
}

public class NewItem
{
    public string i_quality;
    public string i_name_color;
    public string i_classid;
    public string i_instanceid;
    public string i_market_hash_name;
    public string i_market_name;
    public double ui_price;
    public string app;
}

public class Message
{
    public string type;
    public string data;
}

public class TradeResult
{
    public string result;
    public string id;
}

public class HistoryItem
{
    public string i_classid;
    public string i_instanceid;
    public string i_market_hash_name;
    public string i_market_name;
    public double price; //price is measured in kopeykas?  
    public string timesold;
}

public class Auth
{
    public string wsAuth;
    public string success;
}