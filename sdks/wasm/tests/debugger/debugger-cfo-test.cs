using System;

namespace DebuggerTests
{
	public class CallFunctionOnTest {
		public static void PropertyGettersTest ()
		{
			var ptd = new ClassWithProperties { DTAutoProperty = new DateTime (4, 5, 6, 7, 8, 9) };
			var swp = new StructWithProperties ();
			System.Console.WriteLine("break here");
		}

		public static async System.Threading.Tasks.Task PropertyGettersTestAsync ()
		{
			var ptd = new ClassWithProperties { DTAutoProperty = new DateTime (4, 5, 6, 7, 8, 9) };
			var swp = new StructWithProperties ();
			System.Console.WriteLine("break here");
			await System.Threading.Tasks.Task.CompletedTask;
		}
	}

	class ClassWithProperties
	{
		public int Int { get { return 5; } }
		public string String { get { return "foobar"; } }
		public DateTime DT { get { return new DateTime (3, 4, 5, 6, 7, 8); } }

		public int[] IntArray { get { return new int[] { 10, 20 }; } }
		public DateTime[] DTArray { get { return new DateTime[] { new DateTime (6, 7, 8, 9, 10, 11), new DateTime (1, 2, 3, 4, 5, 6) }; }}
		public DateTime DTAutoProperty { get; set; }
		public string StringField;
	}

	struct StructWithProperties
	{
		public int Int { get { return 5; } }
		public string String { get { return "foobar"; } }
		public DateTime DT { get { return new DateTime (3, 4, 5, 6, 7, 8); } }

		public int[] IntArray { get { return new int[] { 10, 20 }; } }
		public DateTime[] DTArray { get { return new DateTime[] { new DateTime (6, 7, 8, 9, 10, 11), new DateTime (1, 2, 3, 4, 5, 6) }; }}
	}
}
