# RPM spec for the experimental Linux package.
#
# scripts/Build-Distribution.ps1 stamps {VERSION} and drives rpmbuild with
# -D app_dir / -D asset_dir / -D icon_dir pointing at the already-published app
# tree and at installer/linux/rpm, so this file is never built straight from a
# checkout (there is no Source). Build it through the distribution script:
#
#   scripts/Build-Distribution.ps1 -Rids linux-x64
#
# The ~ in Version is RPM's prerelease ordering: 1.1.0~beta.11 sorts before
# 1.1.0. It is the same conversion the Debian control file gets, so both package
# formats order prereleases identically.

Name:           cafe-launcher
Version:        {VERSION}
Release:        1%{?dist}
Summary:        Cross-platform launcher for Blue Archive
License:        MIT
URL:            https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia
BuildArch:      x86_64

# The payload is an already-published, self-contained .NET application, so rpm's
# post-install processing must not rewrite it. The rpm package on the Debian-based
# host that runs our release workflow ships no find-debuginfo, which makes the
# default debuginfo/brp pass fail outright; strip and compress have nothing useful
# to do here either way.
%global debug_package %{nil}
%global __os_install_post %{nil}

# Avalonia's X11 backend reaches these libraries through dlopen/P-Invoke, which
# elfdeps cannot see, so they are declared the same way debian/control declares
# its Depends. Soname capabilities are provided by every RPM distribution, so
# this stays portable instead of guessing each distribution's package names.
Requires:       libX11.so.6()(64bit)
Requires:       libICE.so.6()(64bit)
Requires:       libSM.so.6()(64bit)
Requires:       libfontconfig.so.1()(64bit)
Requires:       libxkbcommon.so.0()(64bit)

%description
Experimental Linux package of Cafe Launcher with UMU/Proton and Wine runtime
selection.

%install
rm -rf %{buildroot}
mkdir -p "%{buildroot}/opt/cafe-launcher"
cp -a "%{app_dir}/." "%{buildroot}/opt/cafe-launcher/"
# Mirror the Debian layout: the published tree keeps its own modes, but rpm
# records whatever is in the buildroot, so pin the two exec bits explicitly.
chmod 0755 \
    "%{buildroot}/opt/cafe-launcher/Cafe.Launcher.Avalonia" \
    "%{buildroot}/opt/cafe-launcher/createdump"
install -D -m 0755 "%{asset_dir}/cafe-launcher" "%{buildroot}%{_bindir}/cafe-launcher"
install -D -m 0644 "%{asset_dir}/cafe-launcher.desktop" "%{buildroot}%{_datadir}/applications/cafe-launcher.desktop"
install -D -m 0644 "%{icon_dir}/app-icon-256.png" "%{buildroot}%{_datadir}/icons/hicolor/256x256/apps/cafe-launcher.png"
install -D -m 0644 "%{icon_dir}/app-icon-512.png" "%{buildroot}%{_datadir}/icons/hicolor/512x512/apps/cafe-launcher.png"
install -D -m 0644 "%{app_dir}/LICENSE" "%{buildroot}%{_datadir}/licenses/cafe-launcher/LICENSE"

%files
%{_bindir}/cafe-launcher
%{_datadir}/applications/cafe-launcher.desktop
%{_datadir}/icons/hicolor/256x256/apps/cafe-launcher.png
%{_datadir}/icons/hicolor/512x512/apps/cafe-launcher.png
%license %{_datadir}/licenses/cafe-launcher/LICENSE
/opt/cafe-launcher
