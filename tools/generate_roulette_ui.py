"""Generate the native stationary Roulette cabinet and bounded ball coordinates."""
from pathlib import Path
from math import sin, cos, pi
from xml.dom.minidom import parseString

ROOT = Path(__file__).resolve().parents[1]
UI = ROOT / 'ui/AnoCore'
REDS = {1,3,5,7,9,12,14,16,18,19,21,23,25,27,30,32,34,36}

def label(name, cls='', text='{s:text}'):
    return f'<Label id="casino_roulette_{name}" class="{cls}" text="{text}" hittest="false" />'

def button(name, text, cls=''):
    return f'<Button id="casino_roulette_{name}" class="casino-button {cls}"><Label text="{text}" hittest="false" /></Button>'

def build():
    xml = '''<root><styles><include src="s2r://panorama/styles/custom_game/anocore/ano_casino_roulette.vcss_c" /></styles>
<Panel class="ano-hud-host" hittest="false"><Panel id="casino_roulette_root" class="casino-overlay" hittest="true"><Panel class="casino-dim" hittest="false" /><Panel class="casino-window" hittest="true">
<Panel class="casino-header"><Panel class="casino-cover" hittest="false" /><Label class="casino-title" text="ANOMEME  /  ROULETTE" hittest="false" />'''+button('close','SCHLIESSEN')+'''</Panel><Panel class="casino-accounts">'''
    for name, caption in [('account','PROFILKONTO'),('bank','SPIELGUTHABEN'),('win','RÜCKZAHLUNG')]:
        xml += f'<Panel class="casino-counter"><Label class="caption" text="{caption}" hittest="false" />'+label(name,'amount')+'</Panel>'
    xml += '''</Panel><Panel class="casino-main"><Panel class="wheel-column">'''+label('pointer','casino-pointer','EUROPÄISCHES ROULETTE · EINE NULL')+'''<Panel id="casino_roulette_wheel" class="casino-wheel" hittest="false"><Panel class="casino-wheel-rotor" hittest="false" /><Panel class="wheel-hub" hittest="false">'''+label('wheelresult','casino-wheel-result')+'''<Label class="hub-caption" text="GEFALLENE ZAHL" hittest="false" /></Panel><Panel id="casino_roulette_ball" class="casino-ball" hittest="false" /></Panel></Panel>
<Panel class="betting-table"><Label class="board-title" text="DEIN EINSATZ. DEINE WAHL." hittest="false" /><Panel class="number-table">'''+button('r0','0','zero')+'<Panel class="number-rows">'
    for first in [3,2,1]:
        xml+='<Panel class="number-row">'
        for n in range(first,37,3):
            xml+=button('r'+str(n),str(n),'casino-number '+('red' if n in REDS else 'black'))
        xml+=button('bet'+str(45+first),'2:1','column-bet')+'</Panel>'
    xml+='</Panel></Panel><Panel class="dozens">'
    for n in range(3):
        xml+=button('bet'+str(43+n),str(n+1)+'. DUTZEND','dozen-bet')
    xml+='</Panel><Panel class="outside-bets">'
    for name,text,cls in [(41,'1–18',''),(39,'GERADE',''),(37,'ROT','red'),(38,'SCHWARZ','black'),(40,'UNGERADE',''),(42,'19–36','')]:
        xml+=button('bet'+str(name),text,'outside-bet '+cls)
    xml+='</Panel>'+label('choice','selected-bet')+'<Panel class="number-select"><Label class="caption" text="ZAHL WÄHLEN" hittest="false" />'+button('numberminus','−','small')+label('numbervalue','number-value')+button('numberplus','+','small')+button('number','AUF ZAHL')+'''</Panel><Panel class="paytable" hittest="false"><Label text="ZAHL 36×   ·   DUTZEND / SPALTE 3×   ·   AUSSENWETTE 2×" hittest="false" /><Label text="Rückzahlung inklusive Einsatz · 0 verliert alle Außenwetten" hittest="false" /></Panel></Panel></Panel>'''+label('result','casino-result')+'<Panel class="stake-controls"><Label class="caption" text="EINSATZ" hittest="false" />'+button('minus','−','small')+label('stake','stake-amount')+button('plus','+','small')+button('betmin','MIN','small')+button('bethalf','½','small')+button('betdouble','×2','small')+button('betmax','MAX','small')+button('play','KUGEL STARTEN','casino-primary')+'</Panel>'+label('status','casino-status')+'<Panel class="navigation">'+button('back','SPIELAUSWAHL')+button('refresh','AKTUALISIEREN')+button('rules','SPIELREGELN')+button('retry','BUCHUNG PRÜFEN','retry')+'''</Panel><Label class="note" text="Nur virtuelle € · eine Wette pro Runde · keine Echtgeld-Auszahlung" hittest="false" /></Panel></Panel></Panel></root>'''
    xml='\n'.join(line.rstrip() for line in parseString(xml).toprettyxml(indent='  ').split('\n')[1:] if line.strip())+'\n'
    (UI/'layout/custom_game/anocore/ano_casino_roulette.xml').write_text(xml, encoding='utf-8')
    css='''/* Fixed wheel; only the server-booked ball uses bounded translate coordinates. */
.ano-hud-host, .casino-overlay { width:100%; height:100%; }
.casino-overlay { visibility:collapse; }
.casino-overlay.shown { visibility:visible; }
.casino-dim { width:4000px; height:2400px; horizontal-align:center; vertical-align:center; background-color:#030907ed; }
.casino-window { width:1540px; height:1000px; horizontal-align:center; vertical-align:center; flow-children:down; padding:18px 24px; border:2px solid #aa9462; border-radius:16px; background-color:gradient(linear, 0% 0%, 0% 100%, from(#111c18), to(#070d0b)); }
Label { font-family:Arial; font-size:20px; color:#f0f4ef; margin:0px; }
.casino-header { width:100%; height:50px; flow-children:right; }
.casino-cover { width:42px; height:42px; background-image:url("s2r://panorama/styles/custom_game/anocore/casino/roulette.vtex"); background-size:contain; background-repeat:no-repeat; margin-right:12px; }
.casino-title { width:fill-parent-flow(1); vertical-align:center; font-size:28px; font-weight:bold; color:#e3d2a2; letter-spacing:3px; }
.casino-button { height:44px; padding:8px 14px; margin-right:8px; border:1px solid #587360; border-radius:5px; background-color:#172b22; }
.casino-button Label { font-size:18px; vertical-align:center; horizontal-align:center; }
.casino-button:hover { border-color:#e3d2a2; background-color:#294838; }
.casino-primary { background-color:#236443; border-color:#79c79a; }
.casino-accounts { width:100%; height:76px; flow-children:right; margin-bottom:8px; }
.casino-counter { width:fill-parent-flow(1); height:72px; margin-right:8px; flow-children:down; padding:9px 16px; border:1px solid #425b4b; border-radius:6px; background-color:#101c16; }
.caption { font-size:15px; color:#b5c8bb; letter-spacing:1px; }
.amount { font-size:28px; color:#f0e1b7; margin-top:5px; }
.casino-main { width:100%; height:560px; flow-children:right; }
.wheel-column { width:520px; height:540px; flow-children:down; margin-right:18px; }
.casino-pointer { horizontal-align:center; font-size:17px; color:#d9c794; height:28px; }
.casino-wheel { width:500px; height:500px; min-height:500px; horizontal-align:center; flow-children:none; }
.casino-wheel-rotor { width:500px; height:500px; background-image:url("s2r://panorama/styles/custom_game/anocore/casino/wheel.vtex"); background-size:contain; background-repeat:no-repeat; }
.wheel-hub { width:150px; height:150px; horizontal-align:center; vertical-align:center; flow-children:down; border-radius:75px; border:2px solid #b59b60; background-color:#0a2118; }
.casino-wheel-result { width:100%; height:100px; padding-top:18px; text-align:center; font-size:56px; font-weight:bold; color:#f1e4bb; }
.hub-caption { width:100%; text-align:center; font-size:12px; color:#b5c8bb; }
.land-red .casino-wheel-result { color:#ff959d; }
.land-black .casino-wheel-result { color:#fff6dc; }
.land-green .casino-wheel-result { color:#91efba; }
.casino-ball { width:16px; height:16px; min-width:16px; min-height:16px; border-radius:8px; border:1px solid #ffffff; background-color:#fff8dc; box-shadow:#fff4d299 0px 0px 7px 0px; transform:translateX(242px) translateY(14px); transition-property:transform; transition-duration:0.05s; transition-timing-function:linear; }
.betting-table { width:fill-parent-flow(1); height:550px; flow-children:down; padding:12px; border:2px solid #9c8453; border-radius:20px; background-color:gradient(linear, 0% 0%, 100% 100%, from(#18543f), to(#092f24)); }
.board-title { font-size:23px; color:#e3d2a2; letter-spacing:2px; margin-bottom:8px; }
.number-table { width:100%; flow-children:right; }
.zero { width:52px; height:156px; margin-right:4px; background-color:#1e7451; }
.zero Label { font-size:30px; }
.number-rows { width:fill-parent-flow(1); flow-children:down; }
.number-row { width:100%; height:52px; flow-children:right; }
.casino-number { width:fill-parent-flow(1); height:48px; padding:0px; margin-right:3px; border:1px solid #aa9462; border-radius:3px; }
.casino-number Label { font-size:24px; font-weight:bold; }
.red { background-color:#852b3c; }
.black { background-color:#101c17; }
.column-bet { width:64px; height:48px; padding:0px; margin-right:0px; }
.column-bet Label { font-size:21px; }
.dozens { width:100%; height:48px; flow-children:right; margin-top:4px; }
 .dozen-bet { width:fill-parent-flow(1); height:44px; }
.outside-bets { width:100%; height:48px; flow-children:right; }
.outside-bet { width:fill-parent-flow(1); height:44px; padding:0px; }
.outside-bet Label { font-size:17px; }
.selected { border:3px solid #ffe1a0; box-shadow:#f2ca6c55 0px 0px 7px 0px; }
.selected Label { color:#ffe7a6; }
.selected-bet { width:100%; font-size:24px; color:#ffe1a0; height:34px; padding-top:4px; }
.number-select { width:100%; height:40px; flow-children:right; }
.number-select .casino-button { height:36px; padding:4px 12px; }
.number-select .caption { vertical-align:center; margin-right:14px; }
.small { min-width:52px; padding:8px 12px; }
.number-value { width:54px; text-align:center; vertical-align:center; font-size:23px; }
.paytable { width:100%; flow-children:down; margin-top:5px; }
.paytable Label { width:100%; font-size:16px; color:#d0dccf; margin-top:4px; }
.casino-result { width:100%; height:44px; padding:8px 12px; margin-bottom:8px; background-color:#12271d; border:1px solid #4c6c54; border-radius:6px; font-size:20px; }
.stake-controls { height:52px; horizontal-align:center; flow-children:right; }
.stake-controls .caption { vertical-align:center; margin-right:18px; }
.stake-amount { width:180px; text-align:center; vertical-align:center; font-size:25px; color:#f0e1b7; }
.casino-status { width:100%; height:48px; font-size:18px; white-space:normal; color:#b7dfc5; margin-top:4px; }
.navigation { height:48px; horizontal-align:center; flow-children:right; }
.retry { visibility:collapse; }
.uncertain .retry { visibility:visible; }
.note { horizontal-align:center; font-size:14px; color:#a6b7ac; margin-top:4px; }
.busy .stake-controls, .busy .betting-table { opacity:0.55; }
'''
    for ring in range(6):
        radius=228-16*ring
        for angle in range(148):
            radians=2*pi*angle/148
            x=242+radius*sin(radians)
            y=242-radius*cos(radians)
            css+=f'.ball-frame-{ring*148+angle} {{ transform:translateX({x:.3f}px) translateY({y:.3f}px); }}\n'
    (UI/'styles/custom_game/anocore/ano_casino_roulette.css').write_text(css, encoding='utf-8')

if __name__ == '__main__':
    build()
