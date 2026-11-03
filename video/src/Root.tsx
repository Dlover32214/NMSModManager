import { Composition } from "remotion";
import { AllModsSkyIntro } from "./AllModsSkyIntro";
import { AllModsSkyIntroV2 } from "./AllModsSkyIntroV2";
import { AllModsSkyCover } from "./AllModsSkyCover";

export const RemotionRoot: React.FC = () => {
  return (
    <>
      <Composition
        id="AllModsSkyIntro"
        component={AllModsSkyIntro}
        durationInFrames={1215}
        fps={30}
        width={1920}
        height={1080}
      />
      <Composition
        id="AllModsSkyIntroV2"
        component={AllModsSkyIntroV2}
        durationInFrames={1950}
        fps={30}
        width={1920}
        height={1080}
      />
      <Composition
        id="AllModsSkyCover"
        component={AllModsSkyCover}
        durationInFrames={1}
        fps={30}
        width={1920}
        height={1080}
      />
    </>
  );
};
