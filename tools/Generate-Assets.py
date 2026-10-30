"""Generate original repository artwork and a chart from the published data.

Requires Pillow and matplotlib. No external images/fonts are downloaded.
The health image is a rendered report excerpt, not a captured application UI.
"""
from datetime import datetime
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
import numpy as np

ROOT=Path(__file__).resolve().parent.parent
ASSETS=ROOT/'assets'
SUMMARY=json.loads((ROOT/'results/verified-session.json').read_text())
NAVY='#111c2b'; INK='#12223a'; GREEN='#78df9e'; BLUE='#7acbff'; MUTED='#a0b0c4'


def font(size,bold=False,mono=False):
    names=(['consolab.ttf','consola.ttf'] if mono else ['segoeuib.ttf','segoeui.ttf'] if bold else ['segoeui.ttf'])
    for name in names:
        path=Path('C:/Windows/Fonts')/name
        if path.exists():return ImageFont.truetype(str(path),size)
    try:return ImageFont.truetype('DejaVuSans-Bold.ttf' if bold else 'DejaVuSans.ttf',size)
    except OSError:return ImageFont.load_default()


def banner(name,width=1600,height=570):
    image=Image.new('RGB',(width,height),NAVY);d=ImageDraw.Draw(image)
    for x in range(1000,width,38):d.line((x,0,x,height),fill='#17263a',width=1)
    for y in range(0,height,38):d.line((990,y,width,y),fill='#17263a',width=1)
    d.rectangle((65,58,72,92),fill=GREEN)
    d.text((90,55),'SOURCE / TOOLS / VERIFIED MEASUREMENTS',font=font(23,True),fill=MUTED)
    d.text((65,116),'RTX-MAX',font=font(95,True),fill='#ffffff')
    d.text((70,227),'Windows GPU',font=font(57),fill='#d9e7f9')
    d.text((72,337),'Palit RTX 3060 Ti  |  one verified setup',font=font(27),fill=MUTED)
    d.rounded_rectangle((70,412,830,494),radius=10,fill='#21394a')
    d.text((91,429),'BF16  34.71',font=font(30,True),fill='#becbdc')
    d.text((300,429),'to 63.93 TFLOPS',font=font(30,True),fill=GREEN)
    d.text((601,429),'+84.2%',font=font(30,True),fill=GREEN)
    # Original abstract chip illustration; no manufacturer logo or product photo.
    cx,cy=1250,270
    d.rounded_rectangle((1070,90,1430,450),radius=24,fill='#1c3449',outline='#39516b',width=3)
    for delta in range(-135,150,30):
        for side in (-1,1):
            d.line((cx+delta,cy+side*190,cx+delta,cy+side*216),fill='#54728c',width=7)
            d.line((cx+side*190,cy+delta,cx+side*216,cy+delta),fill='#54728c',width=7)
    d.rounded_rectangle((1130,150,1370,390),radius=18,fill='#152838',outline=GREEN,width=3)
    d.text((1161,218),'MAX',font=font(62,True),fill=GREEN)
    d.text((1091,515),'10DE:2489  /  8 GiB',font=font(24,mono=True),fill=MUTED)
    if height>600:d.text((72,height-67),'Documented experiment. Measured compute gains; application gains vary.',font=font(23),fill=MUTED)
    image.save(ASSETS/name,optimize=True)


def chart():
    order=['BF16','FP16','TF32','FP32']
    rows={row['mode']:row for row in SUMMARY['results']}
    fig,ax=plt.subplots(figsize=(12,5.7),dpi=170)
    fig.patch.set_facecolor('#f4f7fb');ax.set_facecolor('#f4f7fb')
    fig.subplots_adjust(left=.12,right=.81,top=.76,bottom=.25)
    y=np.arange(4);old=[rows[m]['before_tflops'] for m in order];new=[rows[m]['after_tflops'] for m in order]
    ax.barh(y-.16,old,.27,color='#a6b4c7',label='Before')
    ax.barh(y+.16,new,.27,color='#20895c',label='After internal automatic boot')
    ax.set_yticks(y,order,fontweight='bold',fontsize=12,color=INK);ax.invert_yaxis()
    ax.set_xlim(0,75);ax.set_xticks([0,15,30,45,60,75]);ax.tick_params(axis='both',length=0,labelcolor='#56657a',pad=9)
    ax.set_axisbelow(True);ax.grid(axis='x',color='#dfe6ee',linewidth=.8)
    for spine in ax.spines.values():spine.set_visible(False)
    for index,mode in enumerate(order):
        ax.text(old[index]+.7,index-.16,f'{old[index]:.2f}',va='center',fontsize=10,color='#617089')
        ax.text(new[index]+.7,index+.16,f'{new[index]:.2f}',va='center',fontsize=10,color='#155d40',fontweight='bold')
        ax.text(1.06,index,f"+{rows[mode]['gain_percent']:.1f}%",transform=ax.get_yaxis_transform(),va='center',fontsize=17,color='#155d40',fontweight='bold')
    ax.set_xlabel('Dense GEMM throughput / TFLOPS',fontsize=10,color='#56657a',labelpad=12)
    fig.text(.07,.91,'Measured compute, before and after.',fontsize=23,fontweight='bold',color=INK)
    fig.text(.07,.85,'Palit RTX 3060 Ti 8 GiB  /  same driver, libraries and 200 W limit',fontsize=11,color='#56657a')
    fig.legend(*ax.get_legend_handles_labels(),loc='lower left',bbox_to_anchor=(.065,.055),ncol=2,frameon=False,fontsize=10)
    fig.text(.07,.025,'Median of 3 trials per mode. Output correctness passed. One PC; these are not game FPS gains.',fontsize=9,color='#56657a')
    fig.savefig(ASSETS/'results.png',facecolor=fig.get_facecolor())
    fig.savefig(ASSETS/'results.svg',facecolor=fig.get_facecolor())
    plt.close(fig)


def boot():
    image=Image.new('RGB',(1500,390),'#f4f7fb');d=ImageDraw.Draw(image)
    d.text((55,28),'How the reference setup starts',font=font(38,True),fill=INK)
    d.text((56,80),'Schematic / the unlock is reapplied at a full startup',font=font(22),fill='#56657a')
    boxes=[(55,'01','POWER ON','UEFI starts from the internal disk'),(555,'02','LOCAL UNLOCK','Pinned profile + local assets'),(1055,'03','WINDOWS','NVIDIA driver and compute apps')]
    for x,num,title,detail in boxes:
        d.rounded_rectangle((x,148,x+390,290),radius=13,fill=NAVY)
        d.text((x+22,162),num,font=font(22,True),fill=GREEN)
        d.text((x+22,200),title,font=font(29,True),fill='white')
        d.text((x+22,249),detail,font=font(18),fill='#b4c5d9')
    for x in (470,970):
        d.line((x,217,x+60,217),fill='#20895c',width=5)
        d.polygon([(x+60,217),(x+45,207),(x+45,227)],fill='#20895c')
    d.text((56,329),'Separate FAT32 ESP. Windows Boot Manager remains available as fallback.',font=font(23),fill='#56657a')
    image.save(ASSETS/'boot-flow.png',optimize=True)


def health():
    data=json.loads((ROOT/'results/health-example.json').read_text())
    image=Image.new('RGB',(1500,660),NAVY);d=ImageDraw.Draw(image)
    d.text((55,30),'RTX-MAX  /  HEALTH CHECK',font=font(34,True),fill=BLUE)
    d.text((57,89),'Rendered excerpt of actual reference-PC findings. Read-only; no benchmark.',font=font(23),fill=MUTED)
    temp=data['gpu']['temperature_c']
    lines=[('PASS','Windows device','Device error code 0'),('PASS','Temperature',f'{temp:g} C at capture; one instant'),
        ('PASS','Installed files','EFI, profile and five asset hashes match'),('PASS','Internal startup','BootCurrent and internal partition agree'),
        ('INFO','BitLocker','Protection On for checked encrypted volumes'),('INFO','Compatibility','Reference signature matches; other PCs unqualified'),
        ('UNKNOWN','Unlock performance','Not measured by this health check')]
    for index,(status,name,detail) in enumerate(lines):
        y=160+index*54;color=GREEN if status=='PASS' else BLUE if status=='INFO' else MUTED
        d.text((58,y),status,font=font(21,True,True),fill=color)
        d.text((210,y),name,font=font(23,True),fill='#e1eafa')
        d.text((529,y+1),detail,font=font(22),fill='#b4c5d9')
    d.line((57,566,1430,566),fill='#31465b',width=2)
    d.text((57,592),'A healthy device and a verified boot route do not establish an unlocked compute result.',font=font(23),fill=MUTED)
    image.save(ASSETS/'health-check.png',optimize=True)


if __name__=='__main__':
    ASSETS.mkdir(exist_ok=True)
    banner('banner.png');banner('social-preview.png',height=800)
    chart();boot();health()
    print('Generated 6 local assets from published evidence and original schematic artwork.')
