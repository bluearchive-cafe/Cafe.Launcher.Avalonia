(() => {
  const iconPath = "icons.svg";

  document.querySelectorAll("svg[data-icon]").forEach((icon) => {
    const use = document.createElementNS("http://www.w3.org/2000/svg", "use");
    use.setAttribute("href", `${iconPath}#${icon.dataset.icon}`);
    icon.append(use);
  });

  document.querySelectorAll("[data-banner]").forEach((banner) => {
    const slides = [...banner.querySelectorAll(".banner-slide")];
    const dots = [...banner.querySelectorAll(".banner-dot")];
    const previous = banner.querySelector("[data-banner-previous]");
    const next = banner.querySelector("[data-banner-next]");
    let index = 0;
    let manuallyPaused = false;
    let pointerInside = false;
    let windowActive = document.hasFocus();
    let timerId;

    const render = () => {
      slides.forEach((slide, slideIndex) => slide.classList.toggle("active", slideIndex === index));
      dots.forEach((dot, dotIndex) => {
        dot.classList.toggle("active", dotIndex === index);
        dot.setAttribute("aria-current", dotIndex === index ? "true" : "false");
      });
      banner.setAttribute(
        "aria-label",
        `精选横幅，第 ${index + 1} 张，共 ${slides.length} 张。使用左右方向键切换，空格暂停，回车打开。`);
    };

    const move = (direction) => {
      index = (index + direction + slides.length) % slides.length;
      render();
      restart();
    };

    const canAutoplay = () =>
      !manuallyPaused &&
      !pointerInside &&
      windowActive &&
      !document.hidden &&
      !window.matchMedia("(prefers-reduced-motion: reduce)").matches;

    const restart = () => {
      window.clearTimeout(timerId);
      if (canAutoplay()) {
        timerId = window.setTimeout(() => move(1), 6000);
      }
    };

    previous?.addEventListener("click", (event) => {
      event.stopPropagation();
      move(-1);
    });
    next?.addEventListener("click", (event) => {
      event.stopPropagation();
      move(1);
    });
    dots.forEach((dot, dotIndex) => dot.addEventListener("click", (event) => {
      event.stopPropagation();
      index = dotIndex;
      render();
      restart();
    }));

    banner.addEventListener("mouseenter", () => {
      pointerInside = true;
      restart();
    });
    banner.addEventListener("mouseleave", () => {
      pointerInside = false;
      restart();
    });
    banner.addEventListener("keydown", (event) => {
      if (event.key === "ArrowLeft") {
        event.preventDefault();
        move(-1);
      } else if (event.key === "ArrowRight") {
        event.preventDefault();
        move(1);
      } else if (event.key === " ") {
        event.preventDefault();
        manuallyPaused = !manuallyPaused;
        banner.dataset.paused = String(manuallyPaused);
        restart();
      } else if (event.key === "Enter") {
        event.preventDefault();
        banner.dispatchEvent(new CustomEvent("banner-open", { bubbles: true, detail: { index } }));
      }
    });
    document.addEventListener("visibilitychange", restart);
    window.addEventListener("blur", () => {
      windowActive = false;
      restart();
    });
    window.addEventListener("focus", () => {
      windowActive = true;
      restart();
    });
    render();
    restart();
  });

  document.querySelectorAll("[data-drawer-toggle]").forEach((button) => {
    button.addEventListener("click", () => {
      const shell = button.closest(".launcher");
      const isOpen = shell?.classList.toggle("drawer-open") ?? false;
      button.setAttribute("aria-expanded", String(isOpen));
    });
  });

  document.querySelectorAll("[data-dismiss]").forEach((button) => {
    button.addEventListener("click", () => button.closest(".toast-card")?.remove());
  });

  document.querySelectorAll("[data-settings-demo]").forEach((settings) => {
    const state = settings.querySelector("[data-save-state]");
    const error = settings.querySelector("[data-sync-error]");
    const retry = settings.querySelector("[data-retry]");
    let saveTimer;

    const scheduleSave = () => {
      window.clearTimeout(saveTimer);
      if (state) {
        state.classList.remove("error");
        state.textContent = "正在保存…";
      }
      saveTimer = window.setTimeout(() => {
        if (state) state.textContent = "已保存";
      }, 400);
    };

    settings.querySelectorAll("select, input").forEach((control) => {
      control.addEventListener("change", scheduleSave);
      control.addEventListener("input", scheduleSave);
    });

    settings.querySelectorAll("[data-segment]").forEach((button) => {
      button.addEventListener("click", () => {
        const group = button.closest(".segmented");
        group?.querySelectorAll("button").forEach((item) => item.classList.remove("active"));
        button.classList.add("active");
        scheduleSave();
      });
    });

    settings.querySelectorAll("[data-swatch]").forEach((button) => {
      button.addEventListener("click", () => {
        const group = button.closest(".palette");
        group?.querySelectorAll("button").forEach((item) => item.classList.remove("active"));
        button.classList.add("active");
        document.documentElement.style.setProperty("--cafe-accent", button.dataset.color);
        scheduleSave();
      });
    });

    retry?.addEventListener("click", () => {
      error?.setAttribute("hidden", "");
      if (state) {
        state.classList.remove("error");
        state.textContent = "正在保存…";
      }
      window.setTimeout(() => {
        if (state) state.textContent = "已保存";
      }, 400);
    });
  });
})();
