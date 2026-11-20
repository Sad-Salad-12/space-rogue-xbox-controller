// Windows XInput -> verified Space Rogue DOS data fields. No executable hooks.
using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Drawing;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Windows.Forms;

public struct Pad {
 public ushort Buttons; public byte LT,RT; public short LX,LY,RX,RY;
 public bool Neutral {get{return Buttons==0 && LT<30 && RT<30 && Math.Abs((int)LX)<9000 && Math.Abs((int)LY)<9000 && Math.Abs((int)RX)<9000 && Math.Abs((int)RY)<9000;}}
}
public static class Native {
 [StructLayout(LayoutKind.Sequential)] public struct State {public uint Packet;public Pad Pad;}
 [StructLayout(LayoutKind.Sequential)] public struct MBI {public IntPtr Base,Allocation;public uint AllocationProtect;public UIntPtr Size;public uint State,Protect,Type;}
 [StructLayout(LayoutKind.Sequential)] public struct Rect {public int Left,Top,Right,Bottom;}
 [DllImport("xinput1_4.dll")] public static extern uint XInputGetState(uint slot,out State state);
 [DllImport("kernel32.dll",SetLastError=true)] public static extern IntPtr OpenProcess(uint rights,bool inherit,int pid);
 [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
 [DllImport("kernel32.dll")] public static extern UIntPtr VirtualQueryEx(IntPtr p,IntPtr address,out MBI info,UIntPtr size);
 [DllImport("kernel32.dll")] public static extern bool ReadProcessMemory(IntPtr p,IntPtr at,byte[] b,UIntPtr count,out UIntPtr read);
 [DllImport("kernel32.dll")] public static extern bool WriteProcessMemory(IntPtr p,IntPtr at,byte[] b,UIntPtr count,out UIntPtr written);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr w,out uint pid);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr w,out Rect rect);
 [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr w);
}

public sealed class Bridge : IDisposable {
 public const string StockHash="f2d6a4a7980dd91125ef615fcec33427feadafe59595dffd38202d7d34cf4b45";
 static readonly byte[] Signature=Encoding.ASCII.GetBytes("Space Rogue v1.1 - 9/7/89\0");
 public readonly Process Target; IntPtr handle; public long Data; public byte[] Snapshot; public bool OwnsAxes; public string LastError="等待原版图形界面";
 public Bridge(int pid,string game,string dosbox){
  Target=Process.GetProcessById(pid);
  if(!String.Equals(Path.GetFullPath(Target.MainModule.FileName),Path.GetFullPath(dosbox),StringComparison.OrdinalIgnoreCase)||!String.Equals(Target.ProcessName,"dosbox",StringComparison.OrdinalIgnoreCase))throw new Exception("目标不是加载器启动的 DOSBox");
  using(var sha=SHA256.Create()){string h=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Path.Combine(game,"SR.EXE")))).Replace("-","").ToLowerInvariant();if(h!=StockHash)throw new Exception("手柄兼容层仅支持已验证的原版");}
  handle=Native.OpenProcess(0x10|0x20|0x8|0x400,false,pid);if(handle==IntPtr.Zero)throw new Exception("无法访问本次 DOSBox 的游戏内存");
 }
 public bool Alive {get{try{return !Target.HasExited;}catch{return false;}}}
 public bool Focused {get{uint pid;Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out pid);return pid==(uint)Target.Id;}}
 byte[] Read(long at,int count){var b=new byte[count];UIntPtr n;if(!Native.ReadProcessMemory(handle,new IntPtr(at),b,(UIntPtr)count,out n)||n.ToUInt64()!=(ulong)count)return null;return b;}
 public static bool Validate(byte[] b){if(b==null||b.Length<65536)return false;for(int i=0;i<Signature.Length;i++)if(b[94+i]!=Signature[i])return false;if(b[0x2d0]!=3)return false;foreach(int y in new[]{0,1,40,100,198,199})if(BitConverter.ToUInt16(b,0x6bf0+y*2)!=(199-y)*320)return false;return true;}
 public bool Refresh(){if(Data==0)return false;Snapshot=Read(Data,65536);if(!Validate(Snapshot)){Data=0;Snapshot=null;OwnsAxes=false;LastError="游戏场景尚未就绪；暂停写入";return false;}return true;}
 public bool Find(){
  var found=new HashSet<long>();long at=0;Native.MBI info;int regions=0,matches=0;string detail="";
  while(at<0x100000000L && Native.VirtualQueryEx(handle,new IntPtr(at),out info,(UIntPtr)Marshal.SizeOf(typeof(Native.MBI))).ToUInt64()!=0){long start=info.Base.ToInt64(),length=(long)info.Size.ToUInt64(),next=start+length;if(next<=at)break;at=next;
   if(info.State!=0x1000||(info.Protect&0x100)!=0||(info.Protect&0xff)==1||length<65536||length>268435456)continue;
   regions++;for(long part=start;part<next;part+=1024*1024){int count=(int)Math.Min(1024*1024+65536,next-part);var b=Read(part,count);if(b==null)continue;for(int i=94;i+Signature.Length<b.Length;i++){if(b[i]!=Signature[0]||b[i+1]!=Signature[1])continue;bool match=true;for(int j=2;j<Signature.Length;j++)if(b[i+j]!=Signature[j]){match=false;break;}if(!match)continue;matches++;long candidate=part+i-94;var data=Read(candidate,65536);if(data!=null)detail=" mode="+BitConverter.ToUInt16(data,0x2d0)+" row0="+BitConverter.ToUInt16(data,0x6bf0)+" row40="+BitConverter.ToUInt16(data,0x6bf0+80);if(Validate(data))found.Add(candidate);}}
  }
  if(found.Count!=1){LastError=found.Count==0?"等待原版载入 (regions="+regions+", signatures="+matches+detail+")":"原版内存签名不唯一；已停止写入";return false;}Data=found.Single();LastError="原版输入桥已连接";return Refresh();
 }
 public int U8(int offset){return Snapshot==null?0:Snapshot[offset];}
 public bool Flight {get{return Snapshot!=null && U8(0x56e)==1 && U8(0x5fd0)==1;}} // Optional cockpit-cache allocation is not a flight-state flag.
 public bool Hive {get{return Snapshot!=null && !Flight && U8(0x8e82)==2;}}
 void Write(int offset,byte[] b){if(Data==0||offset<0||offset+b.Length>65536)throw new Exception("输入桥未就绪");UIntPtr n;if(!Native.WriteProcessMemory(handle,new IntPtr(Data+offset),b,(UIntPtr)b.Length,out n)||n.ToUInt64()!=(ulong)b.Length)throw new Exception("游戏内存写入失败");}
 public void Axes(int pitch,int yaw,int roll){if(!Flight)return;Write(0x5e87,new[]{unchecked((byte)(sbyte)pitch)});Write(0x5e95,new[]{unchecked((byte)(sbyte)yaw)});Write(0x5e8e,new[]{unchecked((byte)(sbyte)roll)});OwnsAxes=true;}
 public void Release(){if(OwnsAxes&&Refresh()&&Flight)Axes(0,0,0);OwnsAxes=false;}
 public bool Key(byte key){if(Snapshot==null||key==0||U8(0x51d6)!=0||U8(0xe519)!=U8(0x7f97))return false;Write(0x51d6,new[]{key});Snapshot[0x51d6]=key;return true;}
 public void Dispose(){Release();if(handle!=IntPtr.Zero){Native.CloseHandle(handle);handle=IntPtr.Zero;}Target.Dispose();}
}

public static class Mapping {
 public static int Axis(short raw,bool invert){int n=raw;double abs=Math.Abs(n);if(abs<=9000)return 0;int v=(int)Math.Round(Math.Min(1,(abs-9000)/(32767-9000))*70)*Math.Sign(n);return invert?-v:v;}
 public static byte Direction(Pad p){int x=0,y=0;if((p.Buttons&4)!=0||p.LX<-16000)x=-1;if((p.Buttons&8)!=0||p.LX>16000)x=1;if((p.Buttons&1)!=0||p.LY>16000)y=1;if((p.Buttons&2)!=0||p.LY<-16000)y=-1;if(x==0&&y==0)return 0;return (byte)(y==1?(x<0?'7':x>0?'9':'8'):y==-1?(x<0?'1':x>0?'3':'2'):x<0?'4':'6');}
 public static bool Edge(ushort now,ushort old,int mask){return(now&mask)==mask&&(old&mask)!=mask;}
 public static int SelfTest(){int tests=0;Action<bool> check=x=>{if(!x)throw new Exception("映射自检失败 "+tests);tests++;};check(Axis(0,false)==0);check(Axis(8999,false)==0);check(Axis(32767,false)==70);check(Axis(-32768,false)==-70);check(Axis(32767,true)==-70);check(Direction(new Pad{Buttons=9})=='9');check(Direction(new Pad{LX=-32768,LY=-32768})=='1');check(Direction(new Pad())==0);check(Edge(0x1010,0x1000,0x10));check(!Edge(0x1010,0x1010,0x10));var b=new byte[65536];Encoding.ASCII.GetBytes("Space Rogue v1.1 - 9/7/89\0").CopyTo(b,94);b[0x2d0]=3;for(int y=0;y<200;y++)BitConverter.GetBytes((ushort)((199-y)*320)).CopyTo(b,0x6bf0+y*2);check(Bridge.Validate(b));b[0x6bf0]^=1;check(!Bridge.Validate(b));return tests;}
}

public sealed class Progress {public int schema=1;public bool invertPitch=false;}
public sealed class Companion : Form {
 Bridge bridge;Progress progress;string progressFile,statusFile;Timer timer=new Timer();JavaScriptSerializer json=new JavaScriptSerializer();
 bool armed,manualMenu,expanded;int page=1,commandIndex,keyIndex,keyPage;ushort previous;int slot=-1;byte previousDirection;long nextDirection,nextFire,nextThrottle,lastScan,lastStatus;string status="",lastAction="";DateTime started=DateTime.UtcNow;
 Label heading=new Label(),state=new Label(),body=new Label(),footer=new Label();Button close=new Button();Queue<byte> pending=new Queue<byte>();
 const ushort A=0x1000,B=0x2000,X=0x4000,Y=0x8000,LB=0x100,RB=0x200,Menu=0x10,View=0x20;
 readonly string[] commandNames={"返回游戏","新游戏 N","读取旧游戏 O","文字键盘","回答 YES","回答 NO","保存 Ctrl-S","暂停 / 继续 Esc","切换 CF / NF","切换 TAC 显示","切换视角 V","投降 G","抛弃全部货物 J","菜单锁定 开/关","反转俯仰 开/关","退出游戏 Ctrl-Q"};
 readonly byte[] commandKeys={0,78,79,0,89,78,19,27,5,20,86,71,74,0,0,17};
 readonly string[] keyboards={"ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 -_", "abcdefghijklmnopqrstuvwxyz0123456789.,?"};
 public Companion(string[] args){
  if(args.Length!=5)throw new Exception("兼容层需由 MOD 加载器启动");bridge=new Bridge(Int32.Parse(args[0]),args[1],args[2]);
  progressFile=args[3];statusFile=args[4];try{progress=json.Deserialize<Progress>(File.ReadAllText(progressFile));}catch{progress=new Progress();}if(progress==null||progress.schema!=1)progress=new Progress();
  Text="Space Rogue Xbox MOD";FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=true;TopMost=true;BackColor=Color.FromArgb(15,24,27);ForeColor=Color.FromArgb(221,238,226);Font=new Font("Microsoft YaHei UI",10);ClientSize=new Size(490,610);StartPosition=FormStartPosition.Manual;var area=Screen.PrimaryScreen.WorkingArea;Location=new Point(area.Right-ClientSize.Width-15,area.Top+40);
  heading.Location=new Point(18,15);heading.Size=new Size(454,32);heading.Font=new Font(Font.FontFamily,13,FontStyle.Bold);state.Location=new Point(18,52);state.Size=new Size(454,55);state.ForeColor=Color.FromArgb(140,221,170);body.Location=new Point(18,115);body.Size=new Size(454,405);footer.Location=new Point(18,530);footer.Size=new Size(454,65);footer.ForeColor=Color.LightGray;close.Size=new Size(108,29);close.Click+=(s,e)=>{expanded=!expanded;pending.Clear();bridge.Release();Render();};Controls.AddRange(new Control[]{heading,state,body,footer,close});
  timer.Interval=16;timer.Tick+=(s,e)=>Tick();FormClosed+=(s,e)=>{timer.Stop();SaveProgress();bridge.Dispose();};Render();timer.Start();
 }
 protected override bool ShowWithoutActivation {get{return true;}}
 static long Now {get{return Stopwatch.GetTimestamp()*1000/Stopwatch.Frequency;}}
 void SaveProgress(){try{Directory.CreateDirectory(Path.GetDirectoryName(progressFile));File.WriteAllText(progressFile,json.Serialize(progress),Encoding.UTF8);}catch(Exception e){lastAction="手柄设置未保存："+e.Message;}}
 void Queue(byte key,string action){if(key==0)return;if(pending.Count<16)pending.Enqueue(key);lastAction=action;}
 void Tick(){try{
  if(!bridge.Alive){Close();return;}long now=Now;bool ready=bridge.Refresh();if(!ready&&now-lastScan>2000){lastScan=now;ready=bridge.Find();}
  Native.State sample=new Native.State();int connected=-1;for(uint i=0;i<4;i++)if(Native.XInputGetState(i,out sample)==0){connected=(int)i;break;}
  Pad p=connected<0?new Pad():sample.Pad;bool focus=bridge.Focused;
  if(connected!=slot){bridge.Release();pending.Clear();slot=connected;armed=false;previous=p.Buttons;}
  if(slot<0||!focus||!ready){bridge.Release();pending.Clear();armed=false;previous=p.Buttons;status=slot<0?"手柄断开 · 旋转已归零":!focus?"等待原版游戏焦点 · 手柄暂停":bridge.LastError;}
  else if(!armed){previous=p.Buttons;if(p.Neutral){armed=true;status="已就绪";}else status="松开全部按钮和摇杆以启用";}
  else {
   status="XInput #"+(slot+1)+" · "+(manualMenu?"菜单锁定":bridge.Flight?"飞行 / 星门":bridge.Hive?"HIVE":"菜单 / 基地")+" · "+(expanded?"手柄由面板接管":"输入桥已连接");
   bool lb=(p.Buttons&LB)!=0;
   if(Mapping.Edge(p.Buttons,previous,View)){if(lb){manualMenu=!manualMenu;pending.Clear();bridge.Release();lastAction="菜单锁定 "+(manualMenu?"开启":"关闭");}else{expanded=!expanded;page=1;pending.Clear();bridge.Release();}}
   else if(expanded)Panel(p,now);
   else if(!bridge.Flight||manualMenu)MenuInput(p,now);
   else FlightInput(p,now);
   if((!expanded||page==2)&&pending.Count>0&&bridge.Key(pending.Peek()))pending.Dequeue();previous=p.Buttons;
  }
  if(now-lastStatus>500){lastStatus=now;Render();WriteStatus();}
 }catch(Exception e){status="输入已暂停："+e.Message;armed=false;pending.Clear();try{bridge.Release();}catch{}Render();}}
 bool Edge(Pad p,ushort mask){return Mapping.Edge(p.Buttons,previous,mask);}
 byte RepeatDirection(Pad p,long now){byte d=Mapping.Direction(p);if(d==0){previousDirection=0;return 0;}if(d!=previousDirection){previousDirection=d;nextDirection=now+350;return d;}if(now>=nextDirection){nextDirection=now+140;return d;}return 0;}
 void MenuInput(Pad p,long now){bridge.Release();byte d=RepeatDirection(p,now);if(d!=0)Queue(d,"方向 "+(char)d);if(Edge(p,A))Queue(13,"确认 Enter");if(Edge(p,B))Queue(27,"Esc（返回需按原版选项）");if(Edge(p,Y)){expanded=true;page=1;commandIndex=0;}if(Edge(p,Menu))Queue(13,"确认 Enter");if(p.RT>40&&now>=nextFire){nextFire=now+110;Queue(32,"HIVE / 空格");}}
 void FlightInput(Pad p,long now){bool lb=(p.Buttons&LB)!=0;
  bridge.Axes(Mapping.Axis(p.LY,!progress.invertPitch),Mapping.Axis(p.LX,false),Mapping.Axis(p.RX,false));
  if(Math.Abs((int)p.RY)>12000&&now>=nextThrottle){nextThrottle=now+100;Queue((byte)(p.RY>0?'+':'-'),"推力 / 速度");}
  if(p.RT>40&&now>=nextFire){nextFire=now+100;Queue(32,"开火");}
  if(p.LT>40){if(!triggerHeld)Queue(76,"激光模式 L");triggerHeld=true;}else triggerHeld=false;
  if(Edge(p,A))Queue(13,"Enter / 下一目标");if(Edge(p,B)){bridge.Axes(0,0,0);Queue(83,"停止旋转 S");}if(Edge(p,X))Queue((byte)(lb?19:78),lb?"保存 Ctrl-S":"导航 N");if(Edge(p,Y)){if(lb){expanded=true;page=1;commandIndex=0;}else Queue(84,"下一目标 T");}if(Edge(p,RB))Queue(82,"切换武器 R");if(Edge(p,Menu))Queue((byte)(lb?5:27),lb?"切换飞行模式 Ctrl-E":"暂停 Esc");
 }
 bool triggerHeld;
 void Panel(Pad p,long now){bridge.Release();byte d=RepeatDirection(p,now);
  if(Edge(p,B)){expanded=false;return;}
  if(page==2){int size=keyboards[keyPage].Length;if(d=='4')keyIndex=(keyIndex+size-1)%size;if(d=='6')keyIndex=(keyIndex+1)%size;if(d=='8')keyIndex=(keyIndex+size-8)%size;if(d=='2')keyIndex=(keyIndex+8)%size;if(Edge(p,LB)||Edge(p,RB)){keyPage=1-keyPage;keyIndex=Math.Min(keyIndex,keyboards[keyPage].Length-1);}if(Edge(p,A))SendPanelKey((byte)keyboards[keyPage][keyIndex],"输入字符");if(Edge(p,X))SendPanelKey(8,"删除字符");if(Edge(p,Y))SendPanelKey(32,"输入空格");if(Edge(p,Menu)){SendPanelKey(13,"提交文字 Enter");expanded=false;}return;}
  if(page==1){if(d=='8')commandIndex=Math.Max(0,commandIndex-1);if(d=='2')commandIndex=Math.Min(commandNames.Length-1,commandIndex+1);if(Edge(p,A)){int c=commandIndex;if(c==3){page=2;keyIndex=0;}else if(c==13){manualMenu=!manualMenu;lastAction="菜单锁定 "+manualMenu;expanded=false;}else if(c==14){progress.invertPitch=!progress.invertPitch;SaveProgress();lastAction="反转俯仰 "+progress.invertPitch;}else{expanded=false;if(c!=0)Queue(commandKeys[c],commandNames[c]);}}return;}
 }
 void SendPanelKey(byte key,string action){Queue(key,action);if(pending.Count>0&&bridge.Key(pending.Peek()))pending.Dequeue();}
 void Render(){ClientSize=new Size(490,expanded?610:148);body.Visible=expanded;footer.Visible=expanded;close.Visible=true;close.Text=expanded?"关闭面板":"打开面板";close.Location=new Point(365,expanded?570:110);close.BringToFront();heading.Text="Space Rogue · Xbox 控制";state.Text=status+"\n"+lastAction;
  if(!expanded){state.Text+="\nView 手柄面板 · LB+Y 命令页 · LB+View 菜单锁定";return;}
  if(page==2){heading.Text="英文文字键盘";var s=new StringBuilder();string keys=keyboards[keyPage];for(int i=0;i<keys.Length;i++){s.Append(i==keyIndex?"【"+keys[i]+"】":"  "+keys[i]+"  ");if((i+1)%8==0)s.AppendLine().AppendLine();}body.Text=s.ToString();footer.Text="方向键选字 · A 输入 · X 删除 · Y 空格\nLB/RB 换页 · Menu 提交 · B 关闭";}
  else if(page==1){heading.Text="手柄命令页";var s=new StringBuilder();for(int i=0;i<commandNames.Length;i++)s.Append(i==commandIndex?"▶ ":"   ").Append(commandNames[i]).AppendLine();body.Text=s.ToString();footer.Text="上下选择 · A 执行 · B 返回游戏";}
  else{page=1;Render();}
 }
 void WriteStatus(){try{var r=new{schema=1,connected=slot>=0,slot=slot,armed=armed,focused=bridge.Focused,bridgeReady=bridge.Data!=0,flight=bridge.Flight,menuLock=manualMenu,panel=expanded,mode=page,lastAction=lastAction,status=status,elapsedSeconds=(DateTime.UtcNow-started).TotalSeconds};File.WriteAllText(statusFile,json.Serialize(r),Encoding.UTF8);}catch{}}
}
public static class Program {
 [STAThread] public static int Main(string[] args){try{if(args.Length==1&&args[0]=="--self-test"){int n=Mapping.SelfTest();File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test.json"),"{\"passed\":true,\"checks\":"+n+"}");return 0;}Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new Companion(args));return 0;}catch(Exception e){MessageBox.Show(e.Message,"Space Rogue 手柄兼容层",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}}
}
