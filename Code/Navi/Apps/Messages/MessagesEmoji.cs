using System.Text;

namespace NaviApps;

/// <summary>
/// One emoji the picker offers: the character, its shortcode (":smile:", which is what the player can type) and the words
/// the picker's search matches.
/// </summary>
public sealed record EmojiEntry( string Char, string Code, string Words );

/// <summary>
/// A tab of the picker.
/// </summary>
public sealed record EmojiCategory( string Key, string Icon, string Label );

/// <summary>
/// The emoji the Messages app offers, and what it does with them in text. Static data that never changes, so the panel
/// reads it here instead of the state carrying it to every player.
/// </summary>
public static class MessagesEmoji
{
	/// <summary>The category that holds the player's last emoji. It is not in <see cref="Categories"/> data: the state has them.</summary>
	public const string RecentKey = "recent";

	public static IReadOnlyList<EmojiCategory> Categories { get; } =
	[
		new( RecentKey, "\U0001F552", "Recent" ),
		new( "smileys", "\U0001F600", "Smileys" ),
		new( "gestures", "\U0001F44B", "People" ),
		new( "nature", "\U0001F436", "Animals & nature" ),
		new( "food", "\U0001F354", "Food & drink" ),
		new( "activity", "⚽", "Activities" ),
		new( "objects", "\U0001F4A1", "Objects" ),
		new( "symbols", "❤️", "Symbols" ),
	];

	// One emoji per line: the character, its shortcode, then the words that find it.
	static readonly Dictionary<string, string> Data = new()
	{
		["smileys"] = """
			😀 grinning happy
			😃 smiley smile happy
			😄 smile happy laugh
			😁 grin teeth
			😆 laughing satisfied
			😅 sweat_smile nervous
			😂 joy tears laugh lol
			🤣 rofl rolling floor laughing
			🙂 slight_smile
			🙃 upside_down silly
			😉 wink
			😊 blush shy
			😇 innocent angel halo
			🥰 smiling_hearts love adore
			😍 heart_eyes love
			🤩 star_struck wow
			😘 kissing_heart kiss
			😋 yum tasty
			😛 stuck_out_tongue
			😜 zany wink tongue
			🤪 crazy goofy
			😎 sunglasses cool
			🤓 nerd glasses
			🤗 hugging hug
			🤔 thinking hmm
			🤫 shushing quiet secret
			🤐 zipper_mouth
			😐 neutral_face meh
			😑 expressionless
			😶 no_mouth speechless
			😏 smirk
			😒 unamused
			🙄 rolling_eyes
			😬 grimacing awkward
			😌 relieved
			😔 pensive sad
			😪 sleepy
			😴 sleeping zzz
			🤤 drooling
			😷 mask sick
			🤒 thermometer ill
			🤕 bandage hurt
			🤢 nauseated sick
			🤮 vomiting
			🥵 hot sweating
			🥶 cold freezing
			🥳 partying party
			😵 dizzy
			🤯 exploding_head mind_blown
			🥺 pleading puppy
			😕 confused
			😟 worried
			🙁 frowning sad
			😮 open_mouth surprised
			😲 astonished
			😳 flushed embarrassed
			😨 fearful scared
			😰 cold_sweat anxious
			😢 cry sad tear
			😭 sob crying
			😱 scream shocked
			😖 confounded
			😞 disappointed
			😓 downcast_sweat
			😩 weary tired
			😫 tired
			😤 triumph huffing
			😡 rage angry mad
			😠 angry
			🤬 cursing swearing
			😈 smiling_imp devil
			💀 skull dead
			💩 poop
			🤡 clown
			👻 ghost
			👽 alien
			🤖 robot
			""",
		["gestures"] = """
			👋 wave hello bye
			🤚 raised_back_of_hand
			✋ raised_hand stop
			🖖 vulcan spock
			👌 ok_hand okay
			✌️ victory peace
			🤞 crossed_fingers luck
			🤟 love_you
			🤘 metal rock
			👈 point_left
			👉 point_right
			👆 point_up
			👇 point_down
			👍 thumbsup like yes plus
			👎 thumbsdown dislike no minus
			✊ fist
			👊 punch
			👏 clap applause
			🙌 raised_hands hooray
			👐 open_hands
			🤝 handshake deal
			🙏 pray please thanks
			💪 muscle strong flex
			👀 eyes look
			👁️ eye
			👂 ear
			👃 nose
			👅 tongue
			👄 lips mouth
			🧠 brain
			👶 baby
			🧑 person
			👨 man
			👩 woman
			🧓 older_person
			👮 police
			🧙 mage wizard
			🧛 vampire
			🧟 zombie
			🥷 ninja
			""",
		["nature"] = """
			🐶 dog puppy
			🐱 cat kitten
			🐭 mouse
			🐹 hamster
			🐰 rabbit bunny
			🦊 fox
			🐻 bear
			🐼 panda
			🐨 koala
			🐯 tiger
			🦁 lion
			🐮 cow
			🐷 pig
			🐸 frog
			🐵 monkey
			🐔 chicken
			🐧 penguin
			🐦 bird
			🦆 duck
			🦉 owl
			🐺 wolf
			🐴 horse
			🦄 unicorn
			🐝 bee
			🦋 butterfly
			🐌 snail
			🐢 turtle
			🐍 snake
			🐙 octopus
			🐬 dolphin
			🐳 whale
			🦈 shark
			🌵 cactus
			🌲 evergreen tree
			🌳 tree
			🌴 palm
			🌱 seedling
			🍀 clover luck
			🌸 blossom flower
			🌹 rose
			🌻 sunflower
			🌈 rainbow
			⭐ star
			🌙 moon
			☀️ sun
			☁️ cloud
			⚡ zap lightning
			🔥 fire flame lit
			❄️ snowflake
			🌊 wave ocean water
			""",
		["food"] = """
			🍎 apple
			🍊 orange tangerine
			🍋 lemon
			🍌 banana
			🍉 watermelon
			🍇 grapes
			🍓 strawberry
			🍒 cherries
			🍑 peach
			🥑 avocado
			🍅 tomato
			🥕 carrot
			🌽 corn
			🍞 bread
			🧀 cheese
			🥚 egg
			🍳 cooking
			🥓 bacon
			🍔 hamburger burger
			🍟 fries
			🍕 pizza
			🌭 hotdog
			🌮 taco
			🍣 sushi
			🍜 ramen noodles
			🍝 spaghetti pasta
			🍦 icecream
			🍩 doughnut donut
			🍪 cookie
			🎂 birthday cake
			🍫 chocolate
			🍬 candy
			🍿 popcorn
			☕ coffee tea
			🍵 tea
			🍺 beer
			🍷 wine
			🍸 cocktail
			🥛 milk
			🧃 juice
			""",
		["activity"] = """
			⚽ soccer football
			🏀 basketball
			🏈 american_football
			⚾ baseball
			🎾 tennis
			🏐 volleyball
			🏓 pingpong
			🎱 pool 8ball
			🎯 dart target bullseye
			🎳 bowling
			🎮 video_game controller gaming
			🕹️ joystick
			🎲 dice game
			🧩 puzzle
			♟️ chess
			🎨 art palette
			🎬 clapper movie
			🎤 microphone sing
			🎧 headphones music
			🎸 guitar
			🎹 piano keyboard
			🥁 drum
			🎺 trumpet
			🎻 violin
			🏆 trophy win
			🥇 first_place gold
			🥈 second_place silver
			🥉 third_place bronze
			🚗 car
			🚀 rocket
			✈️ airplane
			🚲 bike bicycle
			🏠 house home
			🏰 castle
			🎉 tada party celebrate
			🎊 confetti
			🎁 gift present
			🎈 balloon
			""",
		["objects"] = """
			💡 bulb idea light
			🔦 flashlight
			📱 phone mobile
			💻 laptop computer
			🖥️ desktop monitor
			⌨️ keyboard
			🖱️ mouse_computer
			💾 floppy save
			💿 cd disc
			📷 camera
			📺 tv television
			📻 radio
			🔋 battery
			🔌 plug
			📡 satellite antenna
			🔍 mag search
			🔒 lock locked
			🔓 unlock unlocked
			🔑 key
			🔨 hammer
			🔧 wrench tool
			⚙️ gear settings
			🧲 magnet
			💣 bomb
			🔮 crystal_ball
			💎 gem diamond
			💰 moneybag money
			📦 package box
			📝 memo note write
			📎 paperclip
			📌 pushpin pin
			📅 calendar
			📚 books
			📖 book
			✉️ envelope mail
			🔔 bell notification
			🔕 bell_off mute
			⏰ alarm clock
			⌛ hourglass
			🧭 compass
			""",
		["symbols"] = """
			❤️ heart love red
			🧡 orange_heart
			💛 yellow_heart
			💚 green_heart
			💙 blue_heart
			💜 purple_heart
			🖤 black_heart
			🤍 white_heart
			💔 broken_heart
			💕 two_hearts
			💖 sparkling_heart
			💗 growing_heart
			💘 cupid arrow_heart
			💯 100 perfect
			✨ sparkles
			💫 dizzy_star
			💥 boom collision
			💢 anger
			💤 zzz sleep
			💬 speech_balloon chat
			💭 thought_balloon
			✅ check_mark done
			❌ x cross wrong
			❓ question
			❗ exclamation
			⚠️ warning
			🚫 no_entry prohibited
			➕ plus
			➖ minus
			➡️ arrow_right
			⬅️ arrow_left
			⬆️ arrow_up
			⬇️ arrow_down
			🔁 repeat
			▶️ play
			⏸️ pause
			🔴 red_circle
			🟢 green_circle
			🔵 blue_circle
			⚫ black_circle
			""",
	};

	static IReadOnlyList<EmojiEntry> all;
	static IReadOnlyDictionary<string, IReadOnlyList<EmojiEntry>> byCategory;
	static IReadOnlyDictionary<string, string> byCode;

	/// <summary>Every emoji, in picker order.</summary>
	public static IReadOnlyList<EmojiEntry> All
	{
		get
		{
			Load();
			return all;
		}
	}

	static void Load()
	{
		if ( all is not null )
			return;

		var categories = new Dictionary<string, IReadOnlyList<EmojiEntry>>();
		var codes = new Dictionary<string, string>();
		var everything = new List<EmojiEntry>();

		foreach ( var (key, text) in Data )
		{
			var list = new List<EmojiEntry>();

			foreach ( var line in text.Split( '\n', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries ) )
			{
				var parts = line.Split( ' ', 3, System.StringSplitOptions.RemoveEmptyEntries );
				if ( parts.Length < 2 )
					continue;

				var entry = new EmojiEntry( parts[0], parts[1], parts.Length > 2 ? parts[2] : "" );
				list.Add( entry );
				codes.TryAdd( entry.Code, entry.Char );
			}

			categories[key] = list;
			everything.AddRange( list );
		}

		byCategory = categories;
		byCode = codes;
		all = everything;
	}

	/// <summary>
	/// The emoji of one category, or, with search text, every emoji whose shortcode or words contain it. The Recent tab is
	/// the player's (<paramref name="recent"/>).
	/// </summary>
	public static IReadOnlyList<EmojiEntry> Find( string category, string search, IReadOnlyList<string> recent )
	{
		Load();

		search = search?.Trim().TrimStart( ':' ).ToLowerInvariant() ?? "";
		if ( search.Length > 0 )
			return all.Where( x => x.Code.Contains( search ) || x.Words.Contains( search ) ).Take( 80 ).ToList();

		if ( category == RecentKey )
			return (recent ?? []).Select( x => all.FirstOrDefault( y => y.Char == x ) ).Where( x => x is not null ).ToList();

		return byCategory.TryGetValue( category ?? "", out var list ) ? list : [];
	}

	/// <summary>
	/// Turn ":shortcode:" into the emoji it names, like Discord does. Text that isn't a known shortcode stays as it is.
	/// </summary>
	public static string ReplaceShortcodes( string text )
	{
		if ( string.IsNullOrEmpty( text ) || !text.Contains( ':' ) )
			return text;

		Load();

		var builder = new StringBuilder( text.Length );
		var i = 0;

		while ( i < text.Length )
		{
			if ( text[i] == ':' )
			{
				var end = text.IndexOf( ':', i + 1 );
				if ( end > i + 1 && byCode.TryGetValue( text[(i + 1)..end].ToLowerInvariant(), out var emoji ) )
				{
					builder.Append( emoji );
					i = end + 1;
					continue;
				}
			}

			builder.Append( text[i++] );
		}

		return builder.ToString();
	}

	/// <summary>
	/// Is the text only emoji, a few of them? Discord draws those big.
	/// </summary>
	public static bool IsJumbo( string text )
	{
		if ( string.IsNullOrWhiteSpace( text ) )
			return false;

		var count = 0;

		for ( var i = 0; i < text.Length; i += char.IsHighSurrogate( text[i] ) && i + 1 < text.Length ? 2 : 1 )
		{
			var v = char.IsHighSurrogate( text[i] ) && i + 1 < text.Length ? char.ConvertToUtf32( text[i], text[i + 1] ) : text[i];

			if ( v == 0xFE0F || v == 0x200D || char.IsWhiteSpace( text[i] ) )
				continue;

			var emoji = v is >= 0x1F300 and <= 0x1FAFF || v is >= 0x2600 and <= 0x27BF || v is >= 0x2B00 and <= 0x2BFF
				|| v is 0x2764 or 0x2B50 or 0x203C or 0x2049 or 0x2122 or 0x2139 or 0x231A or 0x231B or 0x23E9 or 0x23F0 or 0x23F3;
			if ( !emoji )
				return false;

			count++;
		}

		return count is > 0 and <= 8;
	}
}
