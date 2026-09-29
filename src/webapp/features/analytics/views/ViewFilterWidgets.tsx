import { Country } from "../dashboard/Country";
import { OS } from "../dashboard/OS";
import { TopNChart } from "../dashboard/TopNChart";
import { TopNDataContainer } from "../dashboard/TopNDataContainer";
import { viewTopAppVersions, viewTopCountries, viewTopOperatingSystems } from "../query";

type Props = {
  appId: string;
};

// The same filters as the dashboard's Countries / Operating Systems / App Versions widgets,
// but counted from the view events so the numbers match what the Views page shows.
export function ViewFilterWidgets(props: Props) {
  return (
    <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
      <div className="rounded-lg border border-border p-4 min-w-0">
        <TopNDataContainer appId={props.appId} queryName="view-top-countries" query={viewTopCountries}>
          {(data) => (
            <TopNChart
              {...data}
              id="view-countries"
              title="Countries"
              searchParamKey="countryCode"
              defaultFormat="percentage"
              valueLabel="Sessions"
              renderRow={(item) => <Country countryCode={item.name} />}
            />
          )}
        </TopNDataContainer>
      </div>
      <div className="rounded-lg border border-border p-4 min-w-0">
        <TopNDataContainer appId={props.appId} queryName="view-top-operatingsystems" query={viewTopOperatingSystems}>
          {(data) => (
            <TopNChart
              {...data}
              id="view-osnames"
              title="Operating Systems"
              searchParamKey="osName"
              defaultFormat="percentage"
              valueLabel="Sessions"
              renderRow={(item) => <OS name={item.name} />}
            />
          )}
        </TopNDataContainer>
      </div>
      <div className="rounded-lg border border-border p-4 min-w-0">
        <TopNDataContainer appId={props.appId} queryName="view-top-appversions" query={viewTopAppVersions}>
          {(data) => (
            <TopNChart
              {...data}
              id="view-appversions"
              title="App Versions"
              searchParamKey="appVersion"
              defaultFormat="percentage"
              valueLabel="Sessions"
            />
          )}
        </TopNDataContainer>
      </div>
    </div>
  );
}
