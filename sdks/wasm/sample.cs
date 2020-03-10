using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using WebAssembly;
using WebAssembly.Net.Http.HttpClient;

public class Math {
	public static async Task<int> IntAdd (int a, int b) {
		var cp = new Simple.Complex (10, "hello");
		int c = a + b;
		int d = c + b;
		int e = d + a;

		await new Math ().FooAsync (e);
		e += cp.DoStuff ();

		return e;
	}

	public int First (int[] x) {
		return x.FirstOrDefault ();
	}

	string math_str = "field value";
	async Task FooAsync (int i)
	{
		string s = "before";
		await Task.Delay(100);
		s = "after";
		First (new int[] {4});
		await Task.Delay(100);
		Console.WriteLine ($"s: {s}");
	}

	public async Task<bool> AsyncMethod0 (string s, int i)
	{
		string local0 = "value0";
		await Task.Delay (10);
		Console.WriteLine ($"* time for the second await, local0: {local0}");
		await AsyncMethodNoReturn ();
		return true;
	}

	public Math ()
	{
		ss_field = new SimpleStruct ("Set in math..ctor");
		ss_field.num = 932;
		ss_field.another_struct.Name = "another_struct's name set as part of ss_field, in math..ctor";

		dt = DateTime.Now;
	}

	DateTime dt;
	SimpleStruct ss_field;
	public async Task AsyncMethodNoReturn ()
	{
		string str = "AsyncMethodNoReturn's local";
		//Console.WriteLine ($"* field f: {f}");
		await Task.Delay (10);
		Console.WriteLine ($"str: {str}, math_Str: {math_str}");
	}

	public static async Task<bool> AsyncTest (string s, int i)
	{
		Console.WriteLine ($"-- AsyncTest ENTER");
		return await new Math().AsyncMethod0 (s, i);
	}

	public static void MethodWithStructs ()
	{
		var ss = new SimpleStruct ("Set in MethodWithStructs, as a local var");
		//ss.gs.StringField = "field in GenericStruct";

		var ss_arr = new SimpleStruct [] { new SimpleStruct ("created for an array") };
		//var gs = new GenericStruct<Math> ();
		Math m = new Math ();
		Console.WriteLine ($"math: {m}");
		Console.WriteLine ($"Using the struct: {ss.dt}, {ss.gs.StringField}, ss_arr: {ss_arr.Length}");
	}

	public static void MethodWithGenericStruct ()
	{
		var gs = new GenericStruct<DateTime> { StringField = "new value set in MethodWithGenericStruct", List = new System.Collections.Generic.List<DateTime> { DateTime.Now } };
		Console.WriteLine ($"MethodWithGenericStruct: {gs.StringField}, {gs.List.Count}");
	}

	//public SimpleStruct SimpleStructProperty { get; set; }

}

public struct SimpleStruct
{
	public uint num;
	public string str_member;
	public DateTime dt;
	public GenericStruct<DateTime> gs;
	//public Math m;
	public AnotherStruct another_struct;

	public SimpleStruct (string str)
	{
		str_member = "Fresh string bytes prepending to the arg: " + str;
		num = 0xDDEEFFAA; //BBCC3377;
		dt = DateTime.Now;
		gs = new GenericStruct<DateTime> { StringField = "new value set in SimpleStruct..ctor", List = new System.Collections.Generic.List<DateTime> { DateTime.Now } };
		//m = new Math ();
		another_struct = new AnotherStruct {
			BoolField = false, Name = "Name for AnotherStruct set in SimpleStruct..ctor",
			RGB = RGB.Green, Options = Options.Option2 };
	}
}

public struct GenericStruct<T>
{
	public System.Collections.Generic.List<T> List;
	public string StringField;
}

public struct AnotherStruct
{
	public bool BoolField;
	public string Name;
	public RGB RGB;
	public Options Options;
}

public enum RGB
{
	Red,
	Green,
	Blue
}

[Flags]
public enum Options
{
	None = 0,
	Option1 = 1,
	Option2 = 2,
	Option3 = 4,

	Default = Option1 | Option3
}

namespace GeoLocation
{
    class Program
    {

        static DOMObject navigator;
        static DOMObject global;
        static string BaseApiUrl = string.Empty;
        static HttpClient httpClient;

        static void Main(string[] args)
        {
            global = new DOMObject(string.Empty);
            navigator = new DOMObject("navigator");

            using (var window = (JSObject)WebAssembly.Runtime.GetGlobalObject("window"))
                using (var location = (JSObject)window.GetObjectProperty("location"))
                {
                    BaseApiUrl = (string)location.GetObjectProperty("origin");
                }

            httpClient = new HttpClient() { BaseAddress = new Uri(BaseApiUrl) };

        }

        static int requests = 0;
        static void GeoFindMe(JSObject output)
        {
            GeoLocation geoLocation;
            try
            {
                geoLocation = new GeoLocation(navigator.GetProperty("geolocation"));
            }
            catch
            {
                output.SetObjectProperty("innerHTML", "<p>Geolocation is not supported by your browser</p>");
                return;
            }

            output.SetObjectProperty("innerHTML", "<p>Locating…</p>");

            geoLocation.OnSuccess += async (object sender, Position position) =>
            {
                using (position)
                {
                    using (var coords = position.Coordinates)
                    {
                        var latitude = coords.Latitude;
                        var longitude = coords.Longitude;

                        output.SetObjectProperty("innerHTML", $"<p>Latitude is {latitude} ° <br>Longitude is {longitude} °</p>");

                        try {

                            var ApiFile = $"https://maps.googleapis.com/maps/api/staticmap?center={latitude},{longitude}&zoom=13&size=300x300&sensor=false";

                            var rspMsg = await httpClient.GetAsync(ApiFile);
                            if (rspMsg.IsSuccessStatusCode)
                            {

                                var mimeType = getMimeType(rspMsg.Content?.ReadAsByteArrayAsync().Result);
                                Console.WriteLine($"Request: {++requests}  ByteAsync: {rspMsg.Content?.ReadAsByteArrayAsync().Result.Length}  MimeType: {mimeType}");
                                global.Invoke("showMyPosition", mimeType, Convert.ToBase64String(rspMsg.Content?.ReadAsByteArrayAsync().Result));
                            }
                            else
                            {
                                output.SetObjectProperty("innerHTML", $"<p>Latitude is {latitude} ° <br>Longitude is {longitude} </p><br>StatusCode: {rspMsg.StatusCode} <br>Response Message: {rspMsg.Content?.ReadAsStringAsync().Result}</p>");
                            }
                        }
                        catch (Exception exc2)
                        {
                            Console.WriteLine($"GeoLocation HttpClient Exception: {exc2.Message}");
                            Console.WriteLine($"GeoLocation HttpClient InnerException: {exc2.InnerException?.Message}");
                        }

                    }
                }

            };

            geoLocation.OnError += (object sender, PositionError e) =>
            {
                output.SetObjectProperty("innerHTML", $"Unable to retrieve your location: Code: {e.Code} - {e.message}");
            };

            geoLocation.GetCurrentPosition();

            geoLocation = null;
        }

        static string getMimeType (byte[] imageData)
        {
            if (imageData.Length < 4)
                return string.Empty;

            if (imageData[0] == 0x89 && imageData[1] == 0x50 && imageData[2] == 0x4E && imageData[3] == 0x47)
                return "image/png";
            else if (imageData[0] == 0xff && imageData[1] == 0xd8)
                return "image/jpeg";
            else if (imageData[0] == 0x47 && imageData[1] == 0x49 && imageData[2] == 0x46)
                return "image/gif";
            else
                return string.Empty;

        }
    }

    // Serves as a wrapper around a JSObject.
    class DOMObject : IDisposable
    {
        public JSObject ManagedJSObject { get; private set; }

        public DOMObject(object jsobject)
        {
            ManagedJSObject = jsobject as JSObject;
            if (ManagedJSObject == null)
                throw new NullReferenceException($"{nameof(jsobject)} must be of type JSObject and non null!");

        }

        public DOMObject(string globalName) : this((JSObject)Runtime.GetGlobalObject(globalName))
        { }

        public object GetProperty(string property)
        {
            return ManagedJSObject.GetObjectProperty(property);
        }

        public object Invoke(string method, params object[] args)
        {
            return ManagedJSObject.Invoke(method, args);
        }

        public void Dispose()
        {
            // Dispose of unmanaged resources.
            Dispose(true);
            // Suppress finalization.
            GC.SuppressFinalize(this);
        }

        // Protected implementation of Dispose pattern.
        protected virtual void Dispose(bool disposing)
        {

            if (disposing)
            {

                // Free any other managed objects here.
                //
            }

            // Free any unmanaged objects here.
            //
            ManagedJSObject?.Dispose();
            ManagedJSObject = null;
        }

    }

    class PositionEventArgs : EventArgs
    {
        public Position Position { get; set; }
    }

    class GeoLocation : DOMObject
    {


        public event EventHandler<Position> OnSuccess;
        public event EventHandler<PositionError> OnError;

        public GeoLocation(object jsobject) : base(jsobject)
        {
        }

        public void GetCurrentPosition()
        {
            var success = new Action<object>((pos) =>
            {
                OnSuccess?.Invoke(this, new Position(pos));
            });

            var error = new Action<object>((err) =>
            {
                OnError?.Invoke(this, new PositionError(err));
            });

            ManagedJSObject.Invoke("getCurrentPosition", success, error);
        }

    }

    class Position : DOMObject
    {

        public Position(object jsobject) : base(jsobject)
        {
        }

        public Coordinates Coordinates => new Coordinates(ManagedJSObject.GetObjectProperty("coords"));

    }

    class PositionError : DOMObject
    {

        public PositionError(object jsobject) : base(jsobject)
        {
        }

        public int Code => (int)ManagedJSObject.GetObjectProperty("code");
        public string message => (string)ManagedJSObject.GetObjectProperty("message");

    }

    class Coordinates : DOMObject
    {

        public Coordinates(object jsobject) : base(jsobject)
        {
        }

        public double Latitude => (double)ManagedJSObject.GetObjectProperty("latitude");
        public double Longitude => (double)ManagedJSObject.GetObjectProperty("longitude");

    }

}
