// Evergrove: search for the housing Decorate panel (housingEditPanel). Appended to the panel's frame 1 script by
// build.py, together with evg_colours.as, so the game's own code stays untouched; the functions below replace the
// panel's AddItem, ResetItems and onClick, because in ActionScript 2 the later definition of a function wins.
//
// A round button with a magnifying glass, left of the first category button, opens a search box. Typing filters the
// current tab's items by name and description; the query stays applied when another tab opens.
function AddItem(guid, iconId, name, desc, quantity, memberOnly, tint)
{
   var item = new Array();
   item.Guid = guid;
   item.Icon = iconId;
   item.Name = name;
   item.Desc = desc;
   item.quantity = quantity;
   item.MemberOnly = memberOnly;
   item.tintValue = tint;
   evgAllItems.push(item);
   evgNoteGuid(guid);
   if(evgMatches(item))
   {
      noItems_mc._visible = false;
      items_mc.addItem(item);
   }
}
function ResetItems()
{
   noItems_mc._visible = true;
   evgAllItems = new Array();
   evgSeen = false;
   items_mc.resetItems();
}
function onClick(btn)
{
   if(btn == optionsButton_mc)
   {
      fscommand("FR_event","OpenOptions()");
   }
   else if(btn == evgSearchBtn)
   {
      evgToggleSearch();
   }
   else if(btn == evgPaletteBtn)
   {
      evgTogglePalette();
   }
}
function evgMatches(item)
{
   if(evgQuery == "")
   {
      return true;
   }
   var text = (item.Name + " " + item.Desc).toLowerCase();
   var words = evgQuery.split(" ");
   var i = 0;
   while(i < words.length)
   {
      if(words[i] != "" && text.indexOf(words[i]) == -1)
      {
         return false;
      }
      i = i + 1;
   }
   return true;
}
function evgApplySearch()
{
   evgQuery = evgSearch_txt.text.toLowerCase();
   items_mc.resetItems();
   var shown = 0;
   var i = 0;
   while(i < evgAllItems.length)
   {
      if(evgMatches(evgAllItems[i]))
      {
         items_mc.addItem(evgAllItems[i]);
         shown = shown + 1;
      }
      i = i + 1;
   }
   noItems_mc._visible = shown == 0;
   items_mc.slotClicked();
}
function evgCreateSearch()
{
   createTextField("evgSearch_txt",900,evgSearchX,evgSearchY,evgSearchW,evgSearchH);
   var format = new TextFormat();
   format.font = "$MainFont";
   format.size = 15;
   format.color = 16777215;
   evgSearch_txt.setNewTextFormat(format);
   evgSearch_txt.embedFonts = true;
   evgSearch_txt.type = "input";
   evgSearch_txt.border = true;
   evgSearch_txt.borderColor = 10077951;
   evgSearch_txt.background = true;
   evgSearch_txt.backgroundColor = 1981774;
   evgSearch_txt.maxChars = 30;
   evgSearch_txt.text = "";
   evgSearch_txt._visible = false;
   evgSearch_txt.onChanged = function()
   {
      _root.evgApplySearch();
   };
   evgSearchBtn = attachMovie("circleButton","evgSearchBtn",901,{_x:evgButtonX,_y:evgButtonY});
   if(evgSearchBtn._width > 0)
   {
      evgSearchBtn._xscale = evgSearchBtn._yscale = evgButtonSize / evgSearchBtn._width * 100;
   }
   evgSearchBtn.addListener(this);
   evgSearchBtn.setCallback("evgSearch");
   evgSearchBtn.setToolTip("Search");
   evgDrawMagnifier();
}
function evgDrawMagnifier()
{
   var bounds = evgSearchBtn.getBounds(evgSearchBtn);
   var r = (bounds.xMax - bounds.xMin) * 0.17;
   var cx = (bounds.xMin + bounds.xMax) / 2 - r * 0.3;
   var cy = (bounds.yMin + bounds.yMax) / 2 - r * 0.3;
   var icon = evgSearchBtn.createEmptyMovieClip("evgIcon",50);
   icon.lineStyle(r * 0.45,16777215,100);
   icon.moveTo(cx + r,cy);
   var step = 1;
   while(step <= 24)
   {
      var a = step / 24 * 2 * Math.PI;
      icon.lineTo(cx + Math.cos(a) * r,cy + Math.sin(a) * r);
      step = step + 1;
   }
   icon.lineStyle(r * 0.6,16777215,100);
   icon.moveTo(cx + r * 0.75,cy + r * 0.75);
   icon.lineTo(cx + r * 1.7,cy + r * 1.7);
}
function evgToggleSearch()
{
   evgSearch_txt._visible = !evgSearch_txt._visible;
   if(evgSearch_txt._visible)
   {
      Selection.setFocus(evgSearch_txt);
   }
   else if(evgSearch_txt.text != "")
   {
      evgSearch_txt.text = "";
      evgApplySearch();
   }
}
var evgAllItems = new Array();
var evgQuery = "";
var evgSearchBtn;
var evgSearchX = 0;
var evgSearchY = 0;
var evgSearchW = 115;
var evgSearchH = 25;
var evgButtonX = 52;
var evgButtonY = 30;
var evgButtonSize = 36;
evgCreateSearch();
