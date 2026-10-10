// Evergrove: a colour bar for the housing Decorate panel (housingEditPanel), built into the same script block as
// evg_search.as. A round button with a palette, left of the search button, opens a box of the game's 38 dye colours.
// The colour is for new parts, and for the Paint button of a placed part's menu (lua_patch.py).
//
// The panel can only tell the game which tray item was clicked, so a swatch sends itemSelected(<command id>) and the
// server reads that id as the colour choice (HousingPalette.CommandId: 0x3F000000 + the dye tint id, 0 for each
// part's own colour). The server answers by showing the tray again in that colour, and every creative tray id carries
// the choice (HousingCreativeCatalog: 0x40000000 | dye << 20 | item), so the bar shows what the server has, whoever
// changed it.
function evgCreatePalette()
{
   evgPaletteBtn = attachMovie("circleButton","evgPaletteBtn",902,{_x:evgButtonX - evgButtonSize - 6,_y:evgButtonY});
   if(evgPaletteBtn._width > 0)
   {
      evgPaletteBtn._xscale = evgPaletteBtn._yscale = evgButtonSize / evgPaletteBtn._width * 100;
   }
   evgPaletteBtn.addListener(this);
   evgPaletteBtn.setCallback("evgPalette");
   evgPaletteBtn.setToolTip("Colours");
   evgDrawPaletteIcon();

   var cols = 10;
   var rows = Math.ceil((evgDyes.length + 1) / cols);
   var w = evgPad * 2 + cols * evgPitch;
   var gridY = evgPad + 20;
   var h = gridY + rows * evgPitch + 24;
   evgPalette = createEmptyMovieClip("evgPalette",903);
   evgPalette._x = evgPaletteX;
   evgPalette._y = evgPaletteY;
   // The background is its own clip: a click handler on evgPalette itself would take the swatches' clicks. Its empty
   // handler keeps clicks on the box from reaching the tray below.
   var background = evgPalette.createEmptyMovieClip("evgBackground",1);
   background.lineStyle(1,10077951,100);
   background.beginFill(1981774,95);
   evgRect(background,0,0,w,h);
   background.endFill();
   background.onPress = function()
   {
   };
   background.useHandCursor = false;

   evgPalette.createTextField("evgLabel",400,evgPad,evgPad - 2,w - evgPad * 2,20);
   var format = new TextFormat();
   format.font = "$MainFont";
   format.size = 14;
   format.color = 16777215;
   evgPalette.evgLabel.setNewTextFormat(format);
   evgPalette.evgLabel.embedFonts = true;
   evgPalette.evgLabel.selectable = false;

   var i = 0;
   while(i <= evgDyes.length)
   {
      var dye = i == 0 ? 0 : evgDyes[i - 1].id;
      var swatch = evgPalette.createEmptyMovieClip("evgSwatch" + i,100 + i);
      swatch._x = evgPad + i % cols * evgPitch;
      swatch._y = gridY + Math.floor(i / cols) * evgPitch;
      if(dye == 0)
      {
         // Each part's own colour: white with a red slash.
         swatch.beginFill(16777215,100);
         evgRect(swatch,0,0,evgSwatchSize,evgSwatchSize);
         swatch.endFill();
         swatch.lineStyle(2,14680064,100);
         swatch.moveTo(1,evgSwatchSize - 1);
         swatch.lineTo(evgSwatchSize - 1,1);
      }
      else
      {
         swatch.lineStyle(1,0,40);
         swatch.beginFill(evgDyes[i - 1].rgb,100);
         evgRect(swatch,0,0,evgSwatchSize,evgSwatchSize);
         swatch.endFill();
      }
      swatch.evgDye = dye;
      swatch.onRelease = function()
      {
         _root.evgSend(this.evgDye);
      };
      swatch.onRollOver = function()
      {
         _root.evgShowLabel(this.evgDye);
      };
      swatch.onRollOut = swatch.onReleaseOutside = function()
      {
         _root.evgShowLabel(_root.evgDye);
      };
      i = i + 1;
   }

   evgRing = evgPalette.createEmptyMovieClip("evgRing",300);
   evgRing.lineStyle(2,16777215,100);
   evgRect(evgRing,-2,-2,evgSwatchSize + 4,evgSwatchSize + 4);

   evgPalette.createTextField("evgHint",401,evgPad,gridY + rows * evgPitch + 2,w - evgPad * 2,20);
   var hint = new TextFormat();
   hint.font = "$MainFont";
   hint.size = 12;
   hint.color = 12632256;
   evgPalette.evgHint.setNewTextFormat(hint);
   evgPalette.evgHint.embedFonts = true;
   evgPalette.evgHint.selectable = false;
   evgPalette.evgHint.text = "To repaint a part: click it, then Paint.";

   evgPalette._visible = false;
   evgRefreshPalette();
}
function evgRect(mc, x, y, w, h)
{
   mc.moveTo(x,y);
   mc.lineTo(x + w,y);
   mc.lineTo(x + w,y + h);
   mc.lineTo(x,y + h);
   mc.lineTo(x,y);
}
function evgDrawPaletteIcon()
{
   var bounds = evgPaletteBtn.getBounds(evgPaletteBtn);
   var size = (bounds.xMax - bounds.xMin) * 0.16;
   var cx = (bounds.xMin + bounds.xMax) / 2;
   var cy = (bounds.yMin + bounds.yMax) / 2;
   var icon = evgPaletteBtn.createEmptyMovieClip("evgIcon",50);
   var colours = [15735322,15456006,1943325,1795005];
   var i = 0;
   while(i < 4)
   {
      var x = cx + (i % 2 == 0 ? - size - 1 : 1);
      var y = cy + (i < 2 ? - size - 1 : 1);
      icon.beginFill(colours[i],100);
      evgRect(icon,x,y,size,size);
      icon.endFill();
      i = i + 1;
   }
}
function evgTogglePalette()
{
   evgPalette._visible = !evgPalette._visible;
}
function evgSend(dye)
{
   // Shown at once; the tray confirms it when the server has changed it.
   evgDye = dye;
   evgRefreshPalette();
   fscommand("FR_event","itemSelected(" + (evgCommandBase + dye) + ")");
}
function evgShowLabel(dye)
{
   var name = "Own colours";
   var i = 0;
   while(i < evgDyes.length)
   {
      if(evgDyes[i].id == dye)
      {
         name = evgDyes[i].name;
      }
      i = i + 1;
   }
   evgPalette.evgLabel.text = "Colour: " + name;
}
function evgRefreshPalette()
{
   var index = 0;
   var i = 0;
   while(i < evgDyes.length)
   {
      if(evgDyes[i].id == evgDye)
      {
         index = i + 1;
      }
      i = i + 1;
   }
   var swatch = evgPalette["evgSwatch" + index];
   evgRing._x = swatch._x;
   evgRing._y = swatch._y;
   evgShowLabel(evgDye);
}
function evgNoteGuid(guid)
{
   var id = Number(guid);
   if(id >= evgRecordBase)
   {
      evgSeen = true;
      evgSeenDye = id >> 20 & 511;
   }
}
function evgAdoptPalette()
{
   if(evgSeen && evgSeenDye != evgDye)
   {
      evgDye = evgSeenDye;
      evgRefreshPalette();
   }
}
function DisplayItems()
{
   evgAdoptPalette();
   items_mc.slotClicked();
}
var evgCommandBase = 1056964608;
var evgRecordBase = 1073741824;
var evgDyes = [{id:227,name:"Rubyburst",rgb:15735322},{id:228,name:"Thunderbird",rgb:12517376},{id:229,name:"Mahogany",rgb:8388608},{id:230,name:"Macaroni",rgb:16761966},{id:231,name:"Sunrise",rgb:16750080},{id:232,name:"Allspice",rgb:8407552},{id:233,name:"Sandrift",rgb:12494472},{id:234,name:"Commoner",rgb:9919292},{id:235,name:"Toasty",rgb:6303768},{id:236,name:"Honeydew",rgb:16249716},{id:237,name:"Turbo",rgb:15455750},{id:238,name:"Lucky",rgb:12036097},{id:239,name:"Electrolime",rgb:13166414},{id:240,name:"Jungle",rgb:9944320},{id:241,name:"Woodland",rgb:6717440},{id:242,name:"Mantis",rgb:7261806},{id:243,name:"Cloverleaf",rgb:1943325},{id:244,name:"Forest",rgb:546312},{id:245,name:"Aqua",rgb:2419435},{id:246,name:"Azure",rgb:48573},{id:247,name:"Sapphire",rgb:32896},{id:248,name:"Icy",rgb:10338031},{id:249,name:"Ocean",rgb:1795005},{id:250,name:"Midnight",rgb:601702},{id:251,name:"Skylight",rgb:8294053},{id:252,name:"Raindrop",rgb:5994401},{id:253,name:"Blizzard",rgb:3887235},{id:254,name:"Amethyst",rgb:12939007},{id:255,name:"Berrybright",rgb:7227830},{id:256,name:"Twilight",rgb:4980864},{id:257,name:"Blossom",rgb:16740037},{id:258,name:"Bubblegum",rgb:14618764},{id:259,name:"Rosepetal",rgb:9896026},{id:260,name:"Stonesurf",rgb:14079702},{id:261,name:"Shadestone",rgb:9737364},{id:262,name:"Stormcloud",rgb:4868682},{id:263,name:"Snowfall",rgb:16777215},{id:264,name:"Darkmatter",rgb:65793}];
var evgDye = 0;
var evgSeen = false;
var evgSeenDye = 0;
var evgPaletteBtn;
var evgPalette;
var evgRing;
var evgPaletteX = 4;
var evgPaletteY = 62;
var evgPad = 6;
var evgPitch = 18;
var evgSwatchSize = 15;
evgCreatePalette();
