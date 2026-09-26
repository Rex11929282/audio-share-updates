#include <pch/pch.hpp>
#include <core/settings.hpp>
#include <core/mcb/mcb_presets.hpp>
#include <cassert>
#include <iostream>
#include <filesystem>
#include <d3d11.h>
using Microsoft::WRL::ComPtr;
static int checks=0;
static void check(bool ok,const char* message){if(!ok)throw std::runtime_error(message);++checks;}
static LRESULT CALLBACK procedure(HWND h,UINT m,WPARAM w,LPARAM l){return DefWindowProcW(h,m,w,l);}
int main(){try {
  config::initialize();settings::finalize_binds();
  const auto initial=config::to_json();check(initial["fields"].size()>100,"real settings registry missing");
  for(auto mode:{mcb::presets::kind::legit,mcb::presets::kind::rage,mcb::presets::kind::hvh}){
    const auto result=mcb::presets::apply_and_save(mode);check(result.success,"native preset save and readback failed");
    check(!(settings::g_combat.m_ragebot.enabled.value&&settings::g_combat.m_legitbot.enabled.value),"combat modes overlap");
    check(config::registry::load(mcb::presets::registry_name(mode)),"native file reload failed");
  }
  check(config::from_json(initial),"restore configuration");
  auto& fields=config::detail::get_registry().fields;
  auto found=std::find_if(fields.begin(),fields.end(),[](const auto& f){return f.ptr==&settings::g_combat.m_ragebot.groups[2].hitchance.value;});
  check(found!=fields.end(),"actual hitchance registration");
  char key[9]{};std::snprintf(key,sizeof(key),"%08x",found->key);
  for(const auto& invalid:{nlohmann::json("bad"),nlohmann::json(1.5),nlohmann::json(999999999999LL),nlohmann::json(nullptr)}){
    auto j=initial;j["fields"][key]=invalid;check(!config::from_json(j),"bad field accepted");check(config::to_json()==initial,"invalid input modified state");
  }
  for(const auto* name:{L"../escape",L"CON",L"AUX.txt",L"COM1",L"x.",L"bad/name"})check(!config::registry::valid_name(name),"unsafe file name accepted");
  std::filesystem::create_directories(config::registry::directory());
  const auto legacy=config::registry::path_for(L"old-format-do-not-overwrite");
  {std::ofstream f(legacy);f<<initial.dump();}
  check(!config::registry::load(L"old-format-do-not-overwrite"),"foreign engine format silently loaded");
  check(!config::registry::save(L"old-format-do-not-overwrite"),"legacy configuration overwritten");
  {std::ifstream f(legacy);nlohmann::json j;f>>j;check(j==initial,"legacy bytes changed");}
  CoInitializeEx(nullptr,COINIT_MULTITHREADED);
  ComPtr<ID3D11Device> device;ComPtr<ID3D11DeviceContext> context;D3D_FEATURE_LEVEL level{};
  check(SUCCEEDED(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&device,&level,&context)),"WARP device creation");
  WNDCLASSW wc{};wc.lpfnWndProc=procedure;wc.hInstance=GetModuleHandleW(nullptr);wc.lpszClassName=L"MCBNativeReview";RegisterClassW(&wc);
  HWND hwnd=CreateWindowW(wc.lpszClassName,L"MCB native widget test",WS_OVERLAPPEDWINDOW,0,0,1000,800,nullptr,nullptr,wc.hInstance,nullptr);
  check(hwnd!=nullptr,"window creation");ShowWindow(hwnd,SW_SHOWNORMAL);SetForegroundWindow(hwnd);check(GetForegroundWindow()==hwnd,"test host could not obtain input focus");check(xdraw::initialize(device.Get(),context.Get()),"actual XDraw initialization");check(xui::initialize(hwnd),"actual XUI initialization");
  xui::setting checkbox{false};
  for(int width:{1440,1920,1440}){
    D3D11_TEXTURE2D_DESC td{};td.Width=width;td.Height=1080;td.MipLevels=1;td.ArraySize=1;td.Format=DXGI_FORMAT_R8G8B8A8_UNORM;td.SampleDesc.Count=1;td.Usage=D3D11_USAGE_DEFAULT;td.BindFlags=D3D11_BIND_RENDER_TARGET|D3D11_BIND_SHADER_RESOURCE;
    ComPtr<ID3D11Texture2D> texture;ComPtr<ID3D11RenderTargetView> target;
    check(SUCCEEDED(device->CreateTexture2D(&td,nullptr,&texture)),"target texture");check(SUCCEEDED(device->CreateRenderTargetView(texture.Get(),nullptr,&target)),"target view");
    auto rtv=target.Get();context->OMSetRenderTargets(1,&rtv,nullptr);D3D11_VIEWPORT vp{0,0,static_cast<float>(width),1080,0,1};context->RSSetViewports(1,&vp);
    const auto frame=[&](bool down,bool released,bool clipped){
      xdraw::begin_frame();xui::begin();auto& input=xui::ctx().input;input.mouse_x=85;input.mouse_y=105;input.mouse_down=down;input.mouse_clicked=down;input.mouse_released=released;
      float x=50,y=50,w=940,h=760;check(xui::begin_window("MCB",x,y,w,h),"native root window");
      auto& dl=xui::draw::current();if(clipped)dl.push_clip(500,500,10,10);
      xui::layout::set_cursor(20,40);const bool clicked=xui::button("按鈕##release_test",120,30);
      if(clipped)dl.pop_clip();xui::end_window();xui::end();xdraw::end_frame();return clicked;
    };
    check(!frame(true,false,false),"button fired on press");check(frame(false,true,false),"button did not fire on release");check(!frame(false,true,false),"duplicate release");
    check(!frame(true,false,true),"clipped press");check(!frame(false,true,false),"press outside clip activated control");
    check(xdraw::viewport_size()==std::pair(width,1080),"render target size mismatch");
  }
  xdraw::shutdown();DestroyWindow(hwnd);CoUninitialize();
  std::cout<<"native_checks="<<checks<<"\nreal_xdraw_xui=true\nwhole_game_menu_tested=false\ngame_loaded=false\n";
  return 0;
}catch(const std::exception& error){std::cerr<<"FAILED: "<<error.what()<<" after "<<checks<<" checks\n";return 1;}}
