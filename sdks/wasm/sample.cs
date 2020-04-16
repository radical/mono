using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using WebAssembly;

public class Math {
	public static int IntAdd (int a, int b) {
		var cp = new Simple.Complex (10, "hello");
		int c = a + b;
		int d = c + b;
		int e = d + a;

		e += cp.DoStuff ();

		return e;
	}


	public int First (int[] x) {
		return x.FirstOrDefault ();
	}


	public static unsafe void TestPointers ()
	{
		// var ss = new string[] { "q", "dfg" };
		// int ivalue0 = 5;
		// int ivalue1 = 10;

		// int* ip = &ivalue0;
		// int** ipp = &ip;
		// int*[] ipa = new int*[] { &ivalue0, &ivalue1, null };

		int[] big = new int[200];
		for (int i = 0; i < big.Length; i ++)
			big[i] = i;

		// DateTime[] big_dt = new DateTime[500];

		// int[][] big_nested = new int[500][];
		// for (int i = 0; i < 500; i ++) {
		// 	big_nested[i] = new int[(i * 10) % 200];
		// 	for (int j = 0; j < big_nested[i].Length; j++)
		// 		big_nested[i][j] = i + j;
		// }


		// char cvalue0 = 'q';
		// char* cp = &cvalue0;

		// DateTime dt = new DateTime(5, 6, 7, 8, 9, 10);
		// void* vp = &dt;
		// DateTime* dtp = &dt;

		// var gs = new GenericStructWithUnmanagedT<DateTime> { Value = new DateTime (1, 2, 3, 4, 5, 6), IntField = 4 };
		// GenericStructWithUnmanagedT<DateTime>* gsp = &gs;

		// Console.WriteLine ($"{(int)*ip}, {(int)**ipp}, {ipa}, {(char)*cp}, {(vp == null ? "null" : "not null")}, {dtp->Second}, {gsp->IntField}");

		// string s = "hello";
		// GenericStructWithUnmanagedT<DateTime> genericStruct = new GenericStructWithUnmanagedT<DateTime> { Value = new DateTime(1, 3, 5, 7, 9, 11), IntField = 99 };

		// TestRef<DateTime>(ref s, ref gs, out var local_m);
		// Console.WriteLine($"{s}, {genericStruct}, {local_m}");
		// Console.WriteLine($"{*cp}, {(int)*ip}, {(int)**ipp} {ipa}, {ss}");
		// Console.WriteLine ($"{gsp->IntField}");
        // Console.WriteLine ($"{big_nested[0][0]}");
		Console.WriteLine ($"{big[40]}");
		// Console.WriteLine($"{(int)*ip}");
		// Console.WriteLine ($"{(int)*ipa[0]}");
        // Console.WriteLine ($"-- {ss[1]}");
        // Console.WriteLine ($"-- {ss[0]}");
        // Console.WriteLine ($"{big_dt[0]}");
        System.Console.WriteLine("foo");
	}

    //FIXME: pointers are method params, return type

    public unsafe static void TestRef<T> (ref string s, ref GenericStructWithUnmanagedT<T> genericStruct, out Math m)
        where T: unmanaged
    {
		Console.WriteLine($"s: {s}, gs: {genericStruct}");
		m = new Math() { StringField = "from TestRef" };
		genericStruct.IntField += 10;
	}

	public string StringField;

	public struct GenericStructWithUnmanagedT<T> where T: unmanaged
	{
		public T Value;
		public int IntField;
	}
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
