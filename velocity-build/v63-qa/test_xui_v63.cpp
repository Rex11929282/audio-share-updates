#include <pch/pch.hpp>
#include <external/xdraw/xui/xui.hpp>
#include <core/rendering/ui_geometry_v63.hpp>
#include <core/localization/zh_tw.hpp>
#include <iostream>
#include <stdexcept>
using Microsoft::WRL::ComPtr;
int tests=0;
void check(bool ok,const char* label){if(!ok)throw std::runtime_error(label);++tests;std::cout<<"PASS "<<label<<std::endl;}
bool near(float a,float b){return std::abs(a-b)<0.02f;}
ComPtr<ID3D11Device> device;ComPtr<ID3D11DeviceContext> context;ComPtr<ID3D11Texture2D> tex;ComPtr<ID3D11RenderTargetView> target;
float px=40,py=40,pw=846,ph=682,cw=1920,ch=1080;
void setup_target(unsigned w,unsigned h){
 context->OMSetRenderTargets(0,nullptr,nullptr);target.Reset();tex.Reset();
 D3D11_TEXTURE2D_DESC d{};d.Width=w;d.Height=h;d.MipLevels=d.ArraySize=1;d.Format=DXGI_FORMAT_R8G8B8A8_UNORM;d.SampleDesc.Count=1;d.BindFlags=D3D11_BIND_RENDER_TARGET;
 if(FAILED(device->CreateTexture2D(&d,nullptr,&tex))||FAILED(device->CreateRenderTargetView(tex.Get(),nullptr,&target)))throw std::runtime_error("render target");
 auto rt=target.Get();context->OMSetRenderTargets(1,&rt,nullptr);D3D11_VIEWPORT vp{0,0,float(w),float(h),0,1};context->RSSetViewports(1,&vp);
}
void message(UINT type,int x,int y){xui::wndproc(type,type==WM_LBUTTONDOWN?MK_LBUTTON:0,MAKELPARAM(short(x),short(y)));}
bool frame(std::function<bool()> content){
 xdraw::begin_frame(true);xdraw::begin_ui_canvas(cw,ch);xui::begin();xdraw::set_ui_blur(0);
 xui::begin_window("##qa_window",px,py,pw,ph,false,846,682,1);
 bool result=content();xui::end_window();xui::end();xdraw::end_ui_canvas();xdraw::end_frame();return result;
}
int main(){
 HWND hwnd=nullptr;
 try{
 CoInitializeEx(nullptr,COINIT_MULTITHREADED);
 WNDCLASSW cls{};cls.lpfnWndProc=DefWindowProcW;cls.hInstance=GetModuleHandleW(nullptr);cls.lpszClassName=L"MCBV63NativeQA";RegisterClassW(&cls);
 hwnd=CreateWindowW(cls.lpszClassName,L"MCB QA",WS_OVERLAPPEDWINDOW,0,0,1000,780,nullptr,nullptr,cls.hInstance,nullptr);check(hwnd!=nullptr,"create isolated Win32 host");
 auto hr=D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&context);check(SUCCEEDED(hr),"create D3D11 WARP renderer");
 setup_target(1440,1080);check(xdraw::initialize(device.Get(),context.Get()),"initialize production XDraw");check(xui::initialize(hwnd),"initialize production XUI");
 auto button=[](){xui::layout::set_cursor(250,90);return xui::button("Save##qa",120,30);};
 frame(button);message(WM_LBUTTONDOWN,330,145);check(!frame(button),"button does not execute on press");
 message(WM_LBUTTONUP,330,145);check(frame(button),"button executes on matching release");check(!frame(button),"button release fires once");
 message(WM_LBUTTONDOWN,330,145);frame(button);message(WM_LBUTTONUP,500,145);check(!frame(button),"button release outside cancels");
 message(WM_LBUTTONDOWN,800,650);frame(button);message(WM_LBUTTONUP,330,145);check(!frame(button),"release into button from outside does not execute");
 message(WM_LBUTTONDOWN,330,145);frame(button);xui::wndproc(WM_KILLFOCUS,0,0);frame(button);message(WM_LBUTTONUP,330,145);check(!frame(button),"focus loss cancels real XUI owner");
 auto clipped=[&](){auto& dl=xui::draw::current();dl.push_clip(40,40,200,60);const bool r=button();dl.pop_clip();return r;};
 message(WM_LBUTTONDOWN,330,145);frame(clipped);message(WM_LBUTTONUP,330,145);check(!frame(clipped),"clipped button cannot receive clicks");
 message(WM_LBUTTONDOWN,330,145);message(WM_LBUTTONUP,330,145);check(frame(button),"real coalesced down-up remains clickable");
 static xui::setting enabled(false,{},"qa enabled","qa");
 auto checkbox=[&](){xui::layout::set_cursor(250,160);return xui::checkbox("enabled##qa",enabled);};
 frame(checkbox);message(WM_LBUTTONDOWN,310,210);frame(checkbox);message(WM_LBUTTONUP,310,210);frame(checkbox);check(enabled.value,"checkbox changes actual bound bool");
 message(WM_MOUSEMOVE,0,0);frame(button);const auto oldx=px,oldy=py;
 message(WM_LBUTTONDOWN,75,60);frame(button);check(near(px,oldx)&&near(py,oldy),"drag press does not jump");
 message(WM_MOUSEMOVE,95,90);frame(button);check(near(px,oldx+20)&&near(py,oldy+30),"drag follows client-coordinate delta");
 message(WM_LBUTTONUP,95,90);frame(button);px=40;py=40;
 message(WM_LBUTTONDOWN,880,716);frame(button);message(WM_MOUSEMOVE,1000,790);frame(button);message(WM_LBUTTONUP,1000,790);frame(button);check(pw==846&&ph==682,"bottom-right drag cannot resize fixed panel");
 for(auto size:{std::pair{1440u,1080u},std::pair{1280u,720u},std::pair{1920u,1080u}}){
  setup_target(size.first,size.second);xdraw::begin_frame(true);xdraw::begin_ui_canvas(cw,ch);
  xdraw::draw_list dl;dl.push_clip(100,100,200,80);dl.rect_filled(100,100,200,80,xdraw::color{255,255,255,255});
  auto m=mcb_ui_v63::mapping::make(cw,ch,float(size.first),float(size.second));
  check(dl.vertices.size()==4&&near(dl.vertices[0].pos[0],100*m.sx)&&near(dl.vertices[0].pos[1],100*m.sy),"production vertices use client-to-target mapping");
  check(dl.commands.size()==1&&dl.commands[0].scissor.left==LONG(std::floor(100*m.sx))&&dl.commands[0].scissor.right==LONG(std::ceil(300*m.sx)),"production scissors use identical coordinate mapping");
  xdraw::end_ui_canvas();message(WM_LBUTTONDOWN,330,145);frame(button);message(WM_LBUTTONUP,330,145);check(frame(button),"click remains aligned after target resolution change");
 }
 setup_target(1440,1080);xdraw::begin_frame(true);xdraw::begin_ui_canvas(cw,ch);
 xdraw::draw_list english,chinese;english.text(0,0,"save",xdraw::color{255,255,255,255});chinese.text(0,0,"儲存",xdraw::color{255,255,255,255});
 bool same=english.vertices.size()==chinese.vertices.size();for(size_t i=0;same&&i<english.vertices.size();++i)same=near(english.vertices[i].pos[0],chinese.vertices[i].pos[0])&&near(english.vertices[i].uv[0],chinese.vertices[i].uv[0]);
 check(same,"rendered translated text and measured text match");xdraw::end_ui_canvas();
 check(localization::tr("remove 3d skybox")=="移除立體天空","Chinese UI terminology resolved");
 std::cout<<"production_xui_tests_passed="<<tests<<"\nsteam_account_runtime=NOT_TESTED\ncs2_runtime=NOT_TESTED\n";
 xdraw::shutdown();DestroyWindow(hwnd);CoUninitialize();return 0;
 }catch(std::exception& e){std::cerr<<"FAIL "<<e.what()<<std::endl;return 1;}
}
