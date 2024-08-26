
// Gardens Point Parser Generator
// Copyright (c) Wayne Kelly, QUT 2005-2014
// (see accompanying GPPGcopyright.rtf)


using System;
using System.Text;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Linq;
using System.Runtime.Serialization;


namespace QUT.GPGen
{
    [Serializable]
	internal abstract class Symbol
	{
        internal readonly string name;

        internal string kind;

		internal abstract int num
        {
            get;
        }

		internal Symbol(string name)
		{
			this.name = name;
		}

		public override string ToString()
		{
			return name;
		}


		internal abstract bool IsNullable();
	}

    [JsonConverter(typeof(SerializationHelper))]
    [Serializable]
	internal class Terminal : Symbol
	{
        class SerializationHelper : JsonConverter<Terminal>
        {
            static string FormatTokenTypes(JsonTokenType[] types)
            {
                const string or = " or ";
                return types.Aggregate(new StringBuilder(),
                        (sb, t) => sb.Append(t.ToString()).Append(or),
                        sb => sb.Remove(sb.Length - or.Length, or.Length)
                            .ToString());
            }

            static void ValidateProperty(ref Utf8JsonReader reader,
                string expected, params JsonTokenType[] types)
            {
                if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new SerializationException(
                        $"Expected property token.");
                }
                if (reader.GetString() != expected)
                {
                    throw new SerializationException(
                        $"Expected property name '{expected}'.");
                }
                if (!reader.Read() || !types.Contains(reader.TokenType))
                {
                    throw new SerializationException(
                        $"Expected property type '{FormatTokenTypes(types)}'.");
                }
            }

            public override Terminal Read(ref Utf8JsonReader reader,
                Type typeToConvert, JsonSerializerOptions options)
            {
                ValidateProperty(ref reader, nameof(Symbol.name),
                    JsonTokenType.String);
                string name = reader.GetString();
                ValidateProperty(ref reader, nameof(symbolic),
                    JsonTokenType.True, JsonTokenType.False);
                bool symbol = reader.GetBoolean();
                ValidateProperty(ref reader, nameof(Terminal.num),
                    JsonTokenType.Number);
                int num = reader.GetInt32();
                ValidateProperty(ref reader, nameof(Alias),
                    JsonTokenType.String, JsonTokenType.Null);
                string/*?*/ alias = reader.GetString();
                if (!reader.Read() || reader.TokenType != JsonTokenType.EndObject)
                {
                    throw new SerializationException(
                        $"Expected end of {nameof(Terminal)} object.");
                }
                return new Terminal(symbol, name, num)
                {
                    Alias = alias
                };
            }

            public override void Write(Utf8JsonWriter writer,
                Terminal value, JsonSerializerOptions options)
            {
                writer.WriteStartObject();
                writer.WriteString(nameof(name), value.name);
                writer.WriteBoolean(nameof(symbolic), value.symbolic);
                writer.WriteNumber(nameof(num), value.num);
                writer.WriteString(nameof(Alias), value.Alias ?? null);
                writer.WriteEndObject();
            }
        }

        static int count;
		static int max;
        internal static int Max { get { return max; } }

        internal Precedence prec;
		private int n;
        internal readonly bool symbolic;
        private string alias;

        internal string Alias 
        {
            get => alias;
            set
            {
                if (alias == null)
                {
                    alias = value;
                }
            }
        } 

		internal override int num
		{
			get
			{
				if (symbolic)
					return max + n;
				else
					return n;
			}
		}

        /// <summary>
        /// If name is an escaped char-lit, it must already be
        /// canonicalized according to some convention. In this 
        /// application CharUtils.Canonicalize().
        /// </summary>
        /// <param name="symbolic">Means "is an ident, not a literal character"</param>
        /// <param name="name">string representation of symbol</param>
		internal Terminal(bool symbolic, string name)
        	: base(name)
        {
			this.symbolic = symbolic;
			if (symbolic)
				this.n = ++count;
			else
			{
				this.n = CharacterUtilities.OrdinalOfCharacterLiteral(name, 1);
				if (n > max) max = n;
			}
		}

        // For use by SerializationHelper only.
        Terminal(bool symbolic, string name, int num)
            : base(name)
        {
            this.symbolic = symbolic;
            if (symbolic && count <= num)
            {
                count = num + 1;
            }
            if (!symbolic && num > max)
            {
                max = num;
            }
        }

        internal static readonly Terminal Ambiguous = new Terminal( true, "$Ambiguous$" );

        internal Terminal(bool symbolic, string name, string alias) 
            : this(symbolic, name)
        {
            if (alias != null)
                this.alias = alias;
        }


		internal override bool IsNullable() { return false;	}

        internal string EnumName() { return base.ToString(); }

        public override string ToString()
        {
            if (this.alias != null)
                return CharacterUtilities.QuoteAndCanonicalize( this.alias );
            else 
                return base.ToString();
        }

        public string BaseString() {
            return base.ToString();
        }

        internal static void InsertMaxDummyTerminalInDictionary( Dictionary<string, Terminal> table ) {
            Terminal maxTerm = null;
            if (Terminal.Max != 0) {
                string maxChr = CharacterUtilities.QuoteMap( Terminal.Max ); // FIXME
                maxTerm = table[maxChr];
            }
            table["@Max@"] = maxTerm;
        }

        internal static void RemoveMaxDummyTerminalFromDictionary( Dictionary<string, Terminal> table ) {
            Terminal maxTerm = table["@Max@"];
            max = (maxTerm != null ? maxTerm.n : 0);
            table.Remove( "@Max@" );
        }

        internal static bool BumpsMax( string str ) {
            string num = CharacterUtilities.CanonicalizeCharacterLiteral( str, 1 );
            int ord = CharacterUtilities.OrdinalOfCharacterLiteral( str, 1 );
            return ord > Terminal.max;
        }
    }


	internal class NonTerminal : Symbol
	{
        internal bool reached;

        // Start experimental features
        internal List<NonTerminal> dependsOnList;
        internal int depth;
        internal bool terminating;
        // end

        static int count;
		private int n;
		internal List<Production> productions = new List<Production>();

		internal NonTerminal(string name)
			: base(name)
		{
            n = ++count;
		}

		internal override int num
		{
			get
			{
				return -n;
			}
		}

		private object isNullable;
		internal override bool IsNullable()
		{
			if (isNullable == null)
			{
				isNullable = false;
				foreach (Production p in productions)
				{
					bool nullable = true;
					foreach (Symbol rhs in p.rhs)
						if (!rhs.IsNullable())
						{
							nullable = false;
							break;
						}
					if (nullable)
					{
						isNullable = true;
						break;
					}
				}
			}

			return (bool)isNullable;
		}
	}
}
